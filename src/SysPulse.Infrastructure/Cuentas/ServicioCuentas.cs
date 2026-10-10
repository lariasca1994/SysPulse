using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Domain;
using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Persistencia;
using SysPulse.Infrastructure.Seguridad;
using SysPulse.Infrastructure.Telegram;

namespace SysPulse.Infrastructure.Cuentas;

public sealed record SolicitudRegistro(string? Nombre, string? Email, string? Password, string? Nivel, string? Telegram, string? Pais);

public sealed record ResultadoRegistro(string UsuarioId, string EnlaceTelegram, string TelegramMostrado, string? EnlaceCorreoDesarrollo);

public enum PasoLogin { CredencialesInvalidas, Bloqueado, Inactivo, FaltaCorreo, FaltaTelegram, CambiarPassword, ConfigurarMfa, Mfa }

public sealed record ResultadoLogin(PasoLogin Paso, UsuarioDoc? Usuario, DateTime? BloqueadoHasta = null);

public sealed record ConfiguracionMfa(string Secreto, string UriQr);

/// <summary>
/// Cuentas de usuario (sección 12): registro propio con nivel N1/N2/N3, verificación de correo y de Telegram,
/// contraseña con hash PBKDF2, bloqueo por intentos, MFA TOTP con códigos de recuperación, y admin semilla.
/// </summary>
public sealed class ServicioCuentas(
    ContextoMongo db,
    Cifrado cifrado,
    ClienteTelegram telegram,
    ServicioCorreo correo,
    Auditoria auditoria,
    OpcionesCuentas opciones)
{
    public const int MaxIntentosLogin = 5;
    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VigenciaInicioTelegram = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VigenciaCodigoTelegram = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VigenciaCorreo = TimeSpan.FromHours(24);
    private const int MaxIntentosCodigo = 5;
    private const int MaxCodigosTelegram = 3;

    private readonly PasswordHasher<UsuarioDoc> _hasher = new();

    // ───────────── Registro ─────────────

    public async Task<(ResultadoRegistro? Resultado, Dictionary<string, string> Errores)> RegistrarAsync(SolicitudRegistro s, string? ip, CancellationToken ct)
    {
        var errores = new Dictionary<string, string>(StringComparer.Ordinal);
        void Validar(string campo, string? error)
        {
            if (error is not null) errores[campo] = error;
        }

        Validar("nombre", ReglasRegistro.ValidarNombre(s.Nombre));
        Validar("email", ReglasRegistro.ValidarEmail(s.Email));
        Validar("password", ReglasRegistro.ValidarPassword(s.Password, s.Email));
        Validar("nivel", ReglasRegistro.ValidarNivel(s.Nivel));
        var (contacto, errorTelegram) = ReglasRegistro.ValidarTelegram(s.Telegram, string.IsNullOrWhiteSpace(s.Pais) ? "CO" : s.Pais!.ToUpperInvariant());
        Validar("telegram", errorTelegram);
        if (errores.Count > 0) return (null, errores);

        var email = s.Email!.Trim().ToLowerInvariant();
        if (await db.Usuarios.Find(u => u.Email == email).AnyAsync(ct).ConfigureAwait(false))
        {
            // Mensaje genérico: no revela si el correo existe con otros datos.
            errores["email"] = "Ya existe una cuenta con ese correo.";
            return (null, errores);
        }

        var usuario = new UsuarioDoc
        {
            Id = ObjectId.GenerateNewId(),
            Nombre = s.Nombre!.Trim(),
            Email = email,
            Rol = s.Nivel!,
            NivelTecnico = s.Nivel!,
            Creado = DateTime.UtcNow,
            Telegram = new TelegramDoc
            {
                Usuario = contacto!.Usuario,
                TelefonoCifrado = contacto.TelefonoE164 is null ? null : cifrado.Cifrar(contacto.TelefonoE164),
                TelefonoMascara = contacto.TelefonoE164 is null ? null : contacto.Mostrar(),
            },
        };
        usuario.PasswordHash = _hasher.HashPassword(usuario, s.Password!);
        await db.Usuarios.InsertOneAsync(usuario, cancellationToken: ct).ConfigureAwait(false);

        var enlaceTelegram = await CrearVerificacionTelegramAsync(usuario.Id, ct).ConfigureAwait(false);
        var enlaceCorreo = await EnviarVerificacionCorreoAsync(usuario, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.registrada", usuario.Id, ip, $"nivel {usuario.Rol}", "usuario", usuario.Id.ToString(), ct).ConfigureAwait(false);

        return (new ResultadoRegistro(usuario.Id.ToString(), enlaceTelegram, contacto.Mostrar(), correo.EnviaDeVerdad ? null : enlaceCorreo), errores);
    }

    // ───────────── Correo ─────────────

    private async Task<string> EnviarVerificacionCorreoAsync(UsuarioDoc usuario, CancellationToken ct)
    {
        var token = Cifrado.TokenAleatorio();
        await db.Verificaciones.InsertOneAsync(new VerificacionDoc
        {
            UsuarioId = usuario.Id,
            Tipo = TipoVerificacion.Correo,
            TokenHash = Cifrado.Hash(token),
            Expira = DateTime.UtcNow + VigenciaCorreo,
            Creado = DateTime.UtcNow,
        }, cancellationToken: ct).ConfigureAwait(false);

        var enlace = $"{opciones.UrlWeb.TrimEnd('/')}/verificar-correo?token={token}";
        var (asunto, html, texto) = PlantillasCorreo.VerificarCorreo(usuario.Nombre, enlace);
        await correo.EnviarAsync(usuario.Email, asunto, html, texto, ct).ConfigureAwait(false);
        return enlace;
    }

    public async Task<bool> VerificarCorreoAsync(string? token, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = Cifrado.Hash(token);
        var v = await db.Verificaciones.FindOneAndDeleteAsync(
            x => x.TokenHash == hash && x.Tipo == TipoVerificacion.Correo && x.Expira > DateTime.UtcNow, cancellationToken: ct).ConfigureAwait(false);
        if (v is null) return false;
        await db.Usuarios.UpdateOneAsync(u => u.Id == v.UsuarioId, Builders<UsuarioDoc>.Update.Set(u => u.EmailVerificado, true), cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.correo_verificado", v.UsuarioId, ip, ct: ct).ConfigureAwait(false);
        return true;
    }

    // ───────────── Telegram ─────────────

    private async Task<string> CrearVerificacionTelegramAsync(ObjectId usuarioId, CancellationToken ct)
    {
        await db.Verificaciones.DeleteManyAsync(v => v.UsuarioId == usuarioId && v.Tipo == TipoVerificacion.Telegram, ct).ConfigureAwait(false);
        var token = Cifrado.TokenAleatorio(24);
        await db.Verificaciones.InsertOneAsync(new VerificacionDoc
        {
            UsuarioId = usuarioId,
            Tipo = TipoVerificacion.Telegram,
            TokenHash = Cifrado.Hash(token),
            Expira = DateTime.UtcNow + VigenciaInicioTelegram,
            Creado = DateTime.UtcNow,
        }, cancellationToken: ct).ConfigureAwait(false);
        return $"https://t.me/{opciones.UsuarioBot}?start={token}";
    }

    /// <summary>Nuevo enlace de verificación (vencido, o la persona cambió de cuenta de Telegram).</summary>
    public async Task<string?> NuevoEnlaceTelegramAsync(string usuarioId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(usuarioId, out var id)) return null;
        var usuario = await db.Usuarios.Find(u => u.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return usuario is null || usuario.Telegram.Verificado ? null : await CrearVerificacionTelegramAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Procesa un mensaje del bot. Un bot no puede escribir primero: la persona pulsa Iniciar desde el enlace
    /// (t.me/bot?start=token). Se confirma la identidad (por @usuario, o compartiendo su propio número) y se envía un código.
    /// </summary>
    public async Task ProcesarTelegramAsync(ActualizacionTelegram actualizacion, CancellationToken ct)
    {
        var mensaje = actualizacion.Mensaje;
        if (mensaje is null || mensaje.Chat.Tipo != "private") return;
        var chatId = mensaje.Chat.Id;

        if (mensaje.Texto?.StartsWith("/start ", StringComparison.Ordinal) == true)
        {
            var hash = Cifrado.Hash(mensaje.Texto["/start ".Length..].Trim());
            var v = await db.Verificaciones.Find(x => x.TokenHash == hash && x.Tipo == TipoVerificacion.Telegram && x.Expira > DateTime.UtcNow)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (v is null)
            {
                await telegram.EnviarAsync(chatId, "Este enlace de verificación no es válido o ya venció. Pide uno nuevo desde SysPulse.", ct: ct).ConfigureAwait(false);
                return;
            }
            var usuario = await db.Usuarios.Find(u => u.Id == v.UsuarioId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (usuario is null) return;

            await db.Verificaciones.UpdateOneAsync(x => x.Id == v.Id, Builders<VerificacionDoc>.Update.Set(x => x.ChatId, chatId), cancellationToken: ct).ConfigureAwait(false);

            if (usuario.Telegram.Usuario is not null)
            {
                if (!ReglasRegistro.CoincideUsuario(usuario.Telegram.Usuario, mensaje.De?.Usuario))
                {
                    await telegram.EnviarAsync(chatId, $"Tu cuenta de Telegram no coincide con @{usuario.Telegram.Usuario}, que es la que registraste en SysPulse. Inicia el bot desde esa cuenta o corrige tu @usuario.", ct: ct).ConfigureAwait(false);
                    return;
                }
                await EnviarCodigoAsync(v.Id, chatId, ct).ConfigureAwait(false);
            }
            else
            {
                await telegram.EnviarAsync(chatId, "Para confirmar que el número es tuyo, toca el botón «📱 Compartir mi número».", pedirContacto: true, ct: ct).ConfigureAwait(false);
            }
            return;
        }

        if (mensaje.Contacto is not null)
        {
            var v = await db.Verificaciones.Find(x => x.ChatId == chatId && x.Tipo == TipoVerificacion.Telegram && x.Expira > DateTime.UtcNow)
                .SortByDescending(x => x.Creado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (v is null) return;
            var usuario = await db.Usuarios.Find(u => u.Id == v.UsuarioId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (usuario?.Telegram.TelefonoCifrado is null) return;

            var esPropio = mensaje.Contacto.UsuarioId is not null && mensaje.Contacto.UsuarioId == mensaje.De?.Id;
            var coincide = ReglasRegistro.CoincideTelefono(cifrado.Descifrar(usuario.Telegram.TelefonoCifrado), mensaje.Contacto.Telefono);
            if (!esPropio || !coincide)
            {
                await telegram.EnviarAsync(chatId, "Ese número no coincide con el que registraste en SysPulse. Comparte el número de esta cuenta de Telegram.", quitarTeclado: true, ct: ct).ConfigureAwait(false);
                return;
            }
            await EnviarCodigoAsync(v.Id, chatId, ct).ConfigureAwait(false);
            return;
        }

        await telegram.EnviarAsync(chatId, "Hola, soy el bot de SysPulse. Para vincular tu cuenta, usa el enlace «Verificar en Telegram» que aparece al registrarte.", ct: ct).ConfigureAwait(false);
    }

    private async Task EnviarCodigoAsync(ObjectId verificacionId, long chatId, CancellationToken ct)
    {
        var v = await db.Verificaciones.Find(x => x.Id == verificacionId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (v is null) return;
        if (v.CodigosEnviados >= MaxCodigosTelegram)
        {
            await telegram.EnviarAsync(chatId, "Se alcanzó el máximo de códigos. Pide un nuevo enlace desde SysPulse.", quitarTeclado: true, ct: ct).ConfigureAwait(false);
            return;
        }
        var codigo = Cifrado.Codigo6();
        await db.Verificaciones.UpdateOneAsync(x => x.Id == verificacionId, Builders<VerificacionDoc>.Update
            .Set(x => x.IdentidadConfirmada, true)
            .Set(x => x.CodigoHash, Cifrado.Hash(codigo))
            .Set(x => x.Intentos, 0)
            .Inc(x => x.CodigosEnviados, 1)
            .Set(x => x.Expira, DateTime.UtcNow + VigenciaCodigoTelegram), cancellationToken: ct).ConfigureAwait(false);
        await telegram.EnviarAsync(chatId, $"Tu código de verificación de SysPulse es: {codigo}\nVence en 10 minutos. No lo compartas con nadie.", quitarTeclado: true, ct: ct).ConfigureAwait(false);
    }

    public async Task<string?> ConfirmarTelegramAsync(string usuarioId, string? codigo, string? ip, CancellationToken ct)
    {
        if (!ObjectId.TryParse(usuarioId, out var id) || string.IsNullOrWhiteSpace(codigo)) return "Código inválido.";
        var v = await db.Verificaciones.Find(x => x.UsuarioId == id && x.Tipo == TipoVerificacion.Telegram && x.IdentidadConfirmada && x.Expira > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (v?.CodigoHash is null || v.ChatId is null) return "Primero abre el enlace en Telegram y sigue las instrucciones del bot.";
        if (v.Intentos >= MaxIntentosCodigo) return "Demasiados intentos. Pide un nuevo enlace.";

        if (!Cifrado.IgualesTiempoConstante(v.CodigoHash, Cifrado.Hash(codigo.Trim())))
        {
            await db.Verificaciones.UpdateOneAsync(x => x.Id == v.Id, Builders<VerificacionDoc>.Update.Inc(x => x.Intentos, 1), cancellationToken: ct).ConfigureAwait(false);
            return "El código no es correcto.";
        }

        if (await db.Usuarios.Find(u => u.Telegram.ChatId == v.ChatId && u.Id != id).AnyAsync(ct).ConfigureAwait(false))
            return "Esa cuenta de Telegram ya está vinculada a otro usuario.";

        await db.Usuarios.UpdateOneAsync(u => u.Id == id, Builders<UsuarioDoc>.Update
            .Set(u => u.Telegram.ChatId, v.ChatId)
            .Set(u => u.Telegram.Verificado, true)
            .Set(u => u.Telegram.VerificadoEn, DateTime.UtcNow), cancellationToken: ct).ConfigureAwait(false);
        await db.Verificaciones.DeleteOneAsync(x => x.Id == v.Id, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.telegram_verificado", id, ip, ct: ct).ConfigureAwait(false);
        await telegram.EnviarAsync(v.ChatId.Value, "✅ Listo: tu Telegram quedó vinculado a SysPulse. Aquí recibirás los avisos de tu nivel.", ct: ct).ConfigureAwait(false);
        return null;
    }

    // ───────────── Inicio de sesión ─────────────

    public async Task<ResultadoLogin> LoginAsync(string? email, string? password, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return new(PasoLogin.CredencialesInvalidas, null);
        var correoNormalizado = email.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.Find(u => u.Email == correoNormalizado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (usuario is null)
        {
            _hasher.HashPassword(new UsuarioDoc(), password); // tiempo similar: no revela si el correo existe
            return new(PasoLogin.CredencialesInvalidas, null);
        }
        if (usuario.BloqueadoHasta > DateTime.UtcNow) return new(PasoLogin.Bloqueado, null, usuario.BloqueadoHasta);

        var resultado = _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password);
        if (resultado == PasswordVerificationResult.Failed)
        {
            var intentos = usuario.IntentosFallidos + 1;
            var bloqueo = intentos >= MaxIntentosLogin ? DateTime.UtcNow + DuracionBloqueo : (DateTime?)null;
            await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
                .Set(u => u.IntentosFallidos, bloqueo is null ? intentos : 0)
                .Set(u => u.BloqueadoHasta, bloqueo), cancellationToken: ct).ConfigureAwait(false);
            await auditoria.RegistrarAsync(bloqueo is null ? "login.fallido" : "login.bloqueado", usuario.Id, ip, ct: ct).ConfigureAwait(false);
            return bloqueo is null ? new(PasoLogin.CredencialesInvalidas, null) : new(PasoLogin.Bloqueado, null, bloqueo);
        }

        if (usuario.IntentosFallidos > 0 || resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var update = Builders<UsuarioDoc>.Update.Set(u => u.IntentosFallidos, 0);
            if (resultado == PasswordVerificationResult.SuccessRehashNeeded) update = update.Set(u => u.PasswordHash, _hasher.HashPassword(usuario, password));
            await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, update, cancellationToken: ct).ConfigureAwait(false);
        }

        if (!usuario.Activo) return new(PasoLogin.Inactivo, null);
        if (!usuario.EmailVerificado) return new(PasoLogin.FaltaCorreo, usuario);
        if (!usuario.Telegram.Verificado && usuario.Rol != Roles.Admin) return new(PasoLogin.FaltaTelegram, usuario);
        if (usuario.DebeCambiarPassword) return new(PasoLogin.CambiarPassword, usuario);
        return new(usuario.Mfa.Activo ? PasoLogin.Mfa : PasoLogin.ConfigurarMfa, usuario);
    }

    public async Task<UsuarioDoc?> BuscarAsync(string usuarioId, CancellationToken ct) =>
        ObjectId.TryParse(usuarioId, out var id) ? await db.Usuarios.Find(u => u.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;

    public async Task<string?> CambiarPasswordInicialAsync(UsuarioDoc usuario, string? nueva, string? ip, CancellationToken ct)
    {
        if (ReglasRegistro.ValidarPassword(nueva, usuario.Email) is { } error) return error;
        if (_hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, nueva!) != PasswordVerificationResult.Failed)
            return "La nueva contraseña debe ser distinta de la temporal.";
        await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
            .Set(u => u.PasswordHash, _hasher.HashPassword(usuario, nueva!))
            .Set(u => u.DebeCambiarPassword, false)
            .Set(u => u.SelloSeguridad, Guid.NewGuid().ToString("N")), cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.password_cambiada", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        return null;
    }

    // ───────────── MFA ─────────────

    public async Task<ConfiguracionMfa> IniciarMfaAsync(UsuarioDoc usuario, CancellationToken ct)
    {
        var secreto = Mfa.NuevoSecreto();
        await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
            .Set(u => u.Mfa.SecretoCifrado, cifrado.Cifrar(secreto))
            .Set(u => u.Mfa.Activo, false), cancellationToken: ct).ConfigureAwait(false);
        return new ConfiguracionMfa(secreto, Mfa.UriConfiguracion(secreto, usuario.Email));
    }

    /// <summary>Activa el MFA con el primer código correcto y devuelve los 10 códigos de recuperación (se muestran una sola vez).</summary>
    public async Task<IReadOnlyList<string>?> ActivarMfaAsync(UsuarioDoc usuario, string? codigo, string? ip, CancellationToken ct)
    {
        var actual = await db.Usuarios.Find(u => u.Id == usuario.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (actual?.Mfa.SecretoCifrado is null || actual.Mfa.Activo) return null;
        var paso = Mfa.Verificar(cifrado.Descifrar(actual.Mfa.SecretoCifrado), codigo, actual.Mfa.UltimoPaso);
        if (paso is null) return null;

        var recuperacion = Mfa.CodigosRecuperacion();
        await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
            .Set(u => u.Mfa.Activo, true)
            .Set(u => u.Mfa.UltimoPaso, paso.Value)
            .Set(u => u.Mfa.CodigosRecuperacionHash, recuperacion.Select(c => Cifrado.Hash(Mfa.NormalizarCodigoRecuperacion(c))).ToList())
            .Set(u => u.SelloSeguridad, Guid.NewGuid().ToString("N"))
            .Set(u => u.UltimaConexion, DateTime.UtcNow), cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.mfa_activado", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        return recuperacion;
    }

    /// <summary>
    /// Cambiar de app autenticadora (teléfono nuevo) desde "Mi cuenta". Exige contraseña y el código actual (o uno de
    /// recuperación, si se perdió el teléfono). El secreto nuevo queda pendiente 10 minutos: el actual sigue valiendo
    /// hasta que se confirme con un código de la app nueva.
    /// </summary>
    public async Task<(ConfiguracionMfa? Configuracion, string? Error)> IniciarCambioMfaAsync(UsuarioDoc usuario, string? password, string? codigo, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (string.IsNullOrEmpty(password) || _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await auditoria.RegistrarAsync("cuenta.mfa_cambio_fallido", usuario.Id, ip, "contraseña", ct: ct).ConfigureAwait(false);
            return (null, "La contraseña no es correcta.");
        }
        if (!await VerificarMfaAsync(usuario, codigo, ip, ct, "mfa_cambio").ConfigureAwait(false))
            return (null, "El código actual no es correcto.");

        var secreto = Mfa.NuevoSecreto();
        await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
            .Set(u => u.Mfa.SecretoPendienteCifrado, cifrado.Cifrar(secreto))
            .Set(u => u.Mfa.PendienteExpira, DateTime.UtcNow.AddMinutes(10)), cancellationToken: ct).ConfigureAwait(false);
        return (new ConfiguracionMfa(secreto, Mfa.UriConfiguracion(secreto, usuario.Email)), null);
    }

    /// <summary>Confirma la app nueva: reemplaza el secreto, genera códigos de recuperación nuevos y cierra las demás sesiones.</summary>
    public async Task<(IReadOnlyList<string>? Codigos, string? Error)> ConfirmarCambioMfaAsync(UsuarioDoc usuario, string? codigo, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var actual = await db.Usuarios.Find(u => u.Id == usuario.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (actual?.Mfa.SecretoPendienteCifrado is null || actual.Mfa.PendienteExpira < DateTime.UtcNow)
            return (null, "El proceso venció (10 minutos). Empieza de nuevo.");
        var secreto = cifrado.Descifrar(actual.Mfa.SecretoPendienteCifrado);
        var paso = Mfa.Verificar(secreto, codigo, 0);
        if (paso is null) return (null, "El código de la app nueva no es correcto. Revisa que hayas escaneado el QR nuevo.");

        var recuperacion = Mfa.CodigosRecuperacion();
        await db.Usuarios.UpdateOneAsync(u => u.Id == usuario.Id, Builders<UsuarioDoc>.Update
            .Set(u => u.Mfa.SecretoCifrado, actual.Mfa.SecretoPendienteCifrado)
            .Set(u => u.Mfa.SecretoPendienteCifrado, null)
            .Set(u => u.Mfa.PendienteExpira, null)
            .Set(u => u.Mfa.Activo, true)
            .Set(u => u.Mfa.UltimoPaso, paso.Value)
            .Set(u => u.Mfa.CodigosRecuperacionHash, recuperacion.Select(c => Cifrado.Hash(Mfa.NormalizarCodigoRecuperacion(c))).ToList())
            .Set(u => u.SelloSeguridad, Guid.NewGuid().ToString("N")), cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.mfa_cambiado", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        if (usuario.Telegram is { Verificado: true, ChatId: { } chat })
        {
            try
            {
                await telegram.EnviarAsync(chat,
                    $"🔐 SysPulse: se configuró una app autenticadora nueva en tu cuenta ({DateTime.UtcNow:dd/MM HH:mm} UTC). La anterior ya no sirve y se cerraron las demás sesiones.\nSi no fuiste tú, avisa al administrador de inmediato.",
                    ct: ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // El cambio ya se aplicó; el aviso es complementario.
            }
        }
        return (recuperacion, null);
    }

    /// <summary>Verifica el segundo factor: código de la app o un código de recuperación (se consume).</summary>
    public async Task<bool> VerificarMfaAsync(UsuarioDoc usuario, string? codigo, string? ip, CancellationToken ct, string evento = "login")
    {
        var actual = await db.Usuarios.Find(u => u.Id == usuario.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (actual?.Mfa.SecretoCifrado is null || !actual.Mfa.Activo || string.IsNullOrWhiteSpace(codigo)) return false;

        var paso = Mfa.Verificar(cifrado.Descifrar(actual.Mfa.SecretoCifrado), codigo.Trim(), actual.Mfa.UltimoPaso);
        if (paso is not null)
        {
            // Condición sobre ultimo_paso: dos peticiones simultáneas con el mismo código no pasan ambas.
            var r = await db.Usuarios.UpdateOneAsync(
                u => u.Id == usuario.Id && u.Mfa.UltimoPaso < paso.Value,
                Builders<UsuarioDoc>.Update.Set(u => u.Mfa.UltimoPaso, paso.Value).Set(u => u.UltimaConexion, DateTime.UtcNow),
                cancellationToken: ct).ConfigureAwait(false);
            if (r.ModifiedCount == 1)
            {
                await auditoria.RegistrarAsync($"{evento}.mfa_ok", usuario.Id, ip, ct: ct).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        var hash = Cifrado.Hash(Mfa.NormalizarCodigoRecuperacion(codigo));
        var consumido = await db.Usuarios.UpdateOneAsync(
            u => u.Id == usuario.Id && u.Mfa.CodigosRecuperacionHash.Contains(hash),
            Builders<UsuarioDoc>.Update.Pull(u => u.Mfa.CodigosRecuperacionHash, hash).Set(u => u.UltimaConexion, DateTime.UtcNow),
            cancellationToken: ct).ConfigureAwait(false);
        if (consumido.ModifiedCount == 1)
        {
            await auditoria.RegistrarAsync($"{evento}.mfa_codigo_recuperacion", usuario.Id, ip, ct: ct).ConfigureAwait(false);
            return true;
        }
        await auditoria.RegistrarAsync($"{evento}.mfa_fallido", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        return false;
    }

    // ───────────── Cambio y restablecimiento de contraseña ─────────────

    private static readonly TimeSpan VigenciaRestablecer = TimeSpan.FromHours(1);
    private const int MaxSolicitudesRestablecer = 3;

    /// <summary>Cambio con sesión iniciada: exige la contraseña actual. Cierra las demás sesiones (nuevo sello).</summary>
    public async Task<string?> CambiarPasswordAsync(UsuarioDoc usuario, string? actual, string? nueva, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (string.IsNullOrEmpty(actual) || _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, actual) == PasswordVerificationResult.Failed)
        {
            await auditoria.RegistrarAsync("cuenta.password_cambio_fallido", usuario.Id, ip, ct: ct).ConfigureAwait(false);
            return "La contraseña actual no es correcta.";
        }
        if (ReglasRegistro.ValidarPassword(nueva, usuario.Email) is { } error) return error;
        if (string.Equals(actual, nueva, StringComparison.Ordinal)) return "La nueva contraseña debe ser distinta de la actual.";

        await GuardarPasswordAsync(usuario, nueva!, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.password_cambiada", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        await AvisarCambioAsync(usuario, ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// "Olvidé mi contraseña": envía un enlace de un solo uso (1 hora). Nunca revela si el correo existe.
    /// Devuelve el enlace solo cuando el correo es simulado (desarrollo), para poder probar el flujo.
    /// </summary>
    public async Task<string?> SolicitarRestablecimientoAsync(string? email, string? ip, CancellationToken ct)
    {
        if (ReglasRegistro.ValidarEmail(email) is not null) return null;
        var correoNormalizado = email!.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.Find(u => u.Email == correoNormalizado && u.Activo && u.EmailVerificado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (usuario is null) return null;

        var vigentes = await db.Verificaciones.CountDocumentsAsync(
            v => v.UsuarioId == usuario.Id && v.Tipo == TipoVerificacion.Restablecer && v.Expira > DateTime.UtcNow, cancellationToken: ct).ConfigureAwait(false);
        if (vigentes >= MaxSolicitudesRestablecer) return null;

        var token = Cifrado.TokenAleatorio();
        await db.Verificaciones.InsertOneAsync(new VerificacionDoc
        {
            UsuarioId = usuario.Id,
            Tipo = TipoVerificacion.Restablecer,
            TokenHash = Cifrado.Hash(token),
            Expira = DateTime.UtcNow + VigenciaRestablecer,
            Creado = DateTime.UtcNow,
        }, cancellationToken: ct).ConfigureAwait(false);

        var enlace = $"{opciones.UrlWeb.TrimEnd('/')}/restablecer?token={token}";
        var (asunto, html, texto) = PlantillasCorreo.RestablecerPassword(usuario.Nombre, enlace);
        await correo.EnviarAsync(usuario.Email, asunto, html, texto, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.restablecimiento_solicitado", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        return correo.EnviaDeVerdad ? null : enlace;
    }

    /// <summary>
    /// Restablece con el enlace del correo y, si la cuenta tiene MFA, el código de la app autenticadora (o uno de
    /// recuperación): quien solo acceda al correo no puede tomar la cuenta. Desbloquea la cuenta y cierra sesiones.
    /// </summary>
    public async Task<string?> RestablecerPasswordAsync(string? token, string? nueva, string? codigo, string? ip, CancellationToken ct)
    {
        const string Invalido = "El enlace no es válido o ya venció. Solicita uno nuevo.";
        if (string.IsNullOrWhiteSpace(token)) return Invalido;
        var hash = Cifrado.Hash(token);
        var v = await db.Verificaciones.Find(x => x.TokenHash == hash && x.Tipo == TipoVerificacion.Restablecer && x.Expira > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (v is null) return Invalido;
        var usuario = await db.Usuarios.Find(u => u.Id == v.UsuarioId && u.Activo).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (usuario is null) return Invalido;
        if (v.Intentos >= MaxIntentosCodigo)
        {
            await db.Verificaciones.DeleteOneAsync(x => x.Id == v.Id, ct).ConfigureAwait(false);
            return "Demasiados intentos con este enlace. Solicita uno nuevo.";
        }

        if (ReglasRegistro.ValidarPassword(nueva, usuario.Email) is { } error) return error;
        if (usuario.Mfa.Activo && !await VerificarMfaAsync(usuario, codigo, ip, ct, "restablecer").ConfigureAwait(false))
        {
            await db.Verificaciones.UpdateOneAsync(x => x.Id == v.Id, Builders<VerificacionDoc>.Update.Inc(x => x.Intentos, 1), cancellationToken: ct).ConfigureAwait(false);
            return "El código de tu app autenticadora no es correcto.";
        }

        await db.Verificaciones.DeleteManyAsync(x => x.UsuarioId == usuario.Id && x.Tipo == TipoVerificacion.Restablecer, ct).ConfigureAwait(false);
        await GuardarPasswordAsync(usuario, nueva!, ct, desbloquear: true).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.password_restablecida", usuario.Id, ip, ct: ct).ConfigureAwait(false);
        await AvisarCambioAsync(usuario, ct).ConfigureAwait(false);
        return null;
    }

    private async Task GuardarPasswordAsync(UsuarioDoc usuario, string nueva, CancellationToken ct, bool desbloquear = false)
    {
        var u = Builders<UsuarioDoc>.Update
            .Set(x => x.PasswordHash, _hasher.HashPassword(usuario, nueva))
            .Set(x => x.DebeCambiarPassword, false)
            .Set(x => x.SelloSeguridad, Guid.NewGuid().ToString("N"));
        if (desbloquear) u = u.Set(x => x.IntentosFallidos, 0).Set(x => x.BloqueadoHasta, null);
        await db.Usuarios.UpdateOneAsync(x => x.Id == usuario.Id, u, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Aviso por Telegram: si la persona no hizo el cambio, se entera de inmediato.</summary>
    private async Task AvisarCambioAsync(UsuarioDoc usuario, CancellationToken ct)
    {
        if (usuario.Telegram is not { Verificado: true, ChatId: { } chat }) return;
        try
        {
            await telegram.EnviarAsync(chat,
                $"🔐 SysPulse: la contraseña de tu cuenta se cambió ({DateTime.UtcNow:dd/MM HH:mm} UTC). Se cerraron las demás sesiones.\nSi no fuiste tú, avisa al administrador de inmediato.",
                ct: ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // El cambio ya se hizo; el aviso es complementario.
        }
    }

    // ───────────── Admin semilla ─────────────

    /// <summary>Crea el admin inicial si no existe (correo y contraseña temporal de variables de entorno).</summary>
    public async Task<bool> SembrarAdminAsync(string? email, string? passwordTemporal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(passwordTemporal)) return false;
        if (await db.Usuarios.Find(u => u.Rol == Roles.Admin).AnyAsync(ct).ConfigureAwait(false)) return false;

        var admin = new UsuarioDoc
        {
            Id = ObjectId.GenerateNewId(),
            Nombre = "Administrador",
            Email = email.Trim().ToLowerInvariant(),
            EmailVerificado = true,
            Rol = Roles.Admin,
            NivelTecnico = Roles.Admin,
            DebeCambiarPassword = true,
            Creado = DateTime.UtcNow,
        };
        admin.PasswordHash = _hasher.HashPassword(admin, passwordTemporal);
        await db.Usuarios.InsertOneAsync(admin, cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.admin_semilla_creado", admin.Id, null, ct: ct).ConfigureAwait(false);
        return true;
    }
}

public sealed record OpcionesCuentas(string UrlWeb, string UsuarioBot);
