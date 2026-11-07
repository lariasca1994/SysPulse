using System.Net;

namespace SysPulse.Infrastructure.Correo;

/// <summary>
/// Plantillas HTML de correo: CSS en línea, una columna, sin JavaScript, sin fuentes ni imágenes remotas,
/// con versión en texto plano. Mismos colores de la plataforma (sección 7).
/// </summary>
public static class PlantillasCorreo
{
    /// <summary>Nombre descriptivo del portal: asunto, encabezado y pie de todos los correos.</summary>
    public const string NombrePortal = "Monitoreo e incidentes TI";

    /// <summary>
    /// Nombre del remitente de todos los correos: siempre el nombre descriptivo del portal. BREVO_SENDER_NAME de la
    /// nube se ignora (puede seguir con el nombre interno anterior); el correo remitente sí sale de la configuración.
    /// </summary>
    public static string NombreRemitente(string? configurado)
    {
        _ = configurado;
        return NombrePortal;
    }

    /// <summary>"Perdí mi autenticador": código para configurar un autenticador nuevo (vence en 15 minutos).</summary>
    public static (string Asunto, string Html, string Texto) CodigoRecuperacionMfa(string nombre, string codigo)
    {
        var asunto = $"{codigo} es tu código para configurar un autenticador nuevo en {NombrePortal}";
        var n = WebUtility.HtmlEncode(nombre);
        var c = WebUtility.HtmlEncode(codigo);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>{NombrePortal}</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 16px;font-size:14px;line-height:1.5;color:#4F5D73">Pediste configurar un autenticador nuevo porque perdiste el anterior. Este código vence en 15 minutos y sirve una sola vez.</p>
              <p style="margin:0 0 20px;font-size:32px;font-weight:700;letter-spacing:8px;color:#0B3A7E">{c}</p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no lo pediste, cambia tu contraseña de inmediato: alguien la conoce. Nunca te pediremos este código por otro medio.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nTu código para configurar un autenticador nuevo en {NombrePortal} es {codigo}. Vence en 15 minutos y sirve una sola vez.\n\nSi no lo pediste, cambia tu contraseña de inmediato.";
        return (asunto, html, texto);
    }

    public static (string Asunto, string Html, string Texto) VerificarCorreo(string nombre, string enlace)
    {
        const string Asunto = "Confirma tu correo en Monitoreo e incidentes TI";
        var n = WebUtility.HtmlEncode(nombre);
        var e = WebUtility.HtmlEncode(enlace);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>Monitoreo e incidentes TI</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 20px;font-size:14px;line-height:1.5;color:#4F5D73">Confirma tu correo para activar tu cuenta. El enlace vence en 24 horas.</p>
              <p style="margin:0 0 20px"><a href="{e}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">Confirmar correo</a></p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no creaste una cuenta en Monitoreo e incidentes TI, ignora este mensaje.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nConfirma tu correo para activar tu cuenta en Monitoreo e incidentes TI (vence en 24 horas):\n{enlace}\n\nSi no creaste una cuenta, ignora este mensaje.";
        return (Asunto, html, texto);
    }

    public static (string Asunto, string Html, string Texto) CodigoVerificacion(string nombre, string codigo)
    {
        var asunto = $"{codigo} es tu código de verificación de Monitoreo e incidentes TI";
        var n = WebUtility.HtmlEncode(nombre);
        var c = WebUtility.HtmlEncode(codigo);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>Monitoreo e incidentes TI</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 16px;font-size:14px;line-height:1.5;color:#4F5D73">Este es tu código de verificación en dos pasos. Vence en 10 minutos y sirve una sola vez.</p>
              <p style="margin:0 0 20px;font-size:32px;font-weight:700;letter-spacing:8px;color:#0B3A7E">{c}</p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no intentaste ingresar, cambia tu contraseña: alguien la conoce. Nunca te pediremos este código por otro medio.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nTu código de verificación de Monitoreo e incidentes TI es {codigo}. Vence en 10 minutos y sirve una sola vez.\n\nSi no intentaste ingresar, cambia tu contraseña.";
        return (asunto, html, texto);
    }

    public static (string Asunto, string Html, string Texto) RestablecerPassword(string nombre, string enlace)
    {
        const string Asunto = "Restablece tu contraseña de Monitoreo e incidentes TI";
        var n = WebUtility.HtmlEncode(nombre);
        var e = WebUtility.HtmlEncode(enlace);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>Monitoreo e incidentes TI</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 20px;font-size:14px;line-height:1.5;color:#4F5D73">Recibimos una solicitud para restablecer tu contraseña. El enlace vence en 1 hora y sirve una sola vez. Al guardar la contraseña nueva se cierran tus sesiones abiertas; tu verificación en dos pasos sigue activa.</p>
              <p style="margin:0 0 20px"><a href="{e}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">Restablecer contraseña</a></p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no lo solicitaste, ignora este mensaje: tu contraseña no cambia.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nPara restablecer tu contraseña de Monitoreo e incidentes TI abre este enlace (vence en 1 hora y sirve una sola vez):\n{enlace}\n\nSi no lo solicitaste, ignora este mensaje.";
        return (Asunto, html, texto);
    }

    /// <summary>Aviso de incidente o excepción. Tono: critico | advertencia | ok | info (color de la franja).</summary>
    public static (string Asunto, string Html, string Texto) Aviso(Notificaciones.ContenidoCorreo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var color = c.Tono switch { "critico" => "#C8231A", "advertencia" => "#FFBE00", "ok" => "#1A7335", _ => "#006AA3" };
        var filas = string.Concat(c.Filas.Select(f => $"""
            <tr><td style="padding:8px 0;border-top:1px solid #DDE3EC;font-size:13px;color:#4F5D73;width:40%">{WebUtility.HtmlEncode(f.Etiqueta)}</td>
            <td style="padding:8px 0;border-top:1px solid #DDE3EC;font-size:13px;font-weight:600;text-align:right">{WebUtility.HtmlEncode(f.Valor)}</td></tr>
            """));
        var boton = c.Enlace is null ? "" : $"""
            <p style="margin:20px 0 0"><a href="{WebUtility.HtmlEncode(c.Enlace)}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">{WebUtility.HtmlEncode(c.TextoEnlace ?? "Abrir en Monitoreo e incidentes TI")}</a></p>
            """;
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>Monitoreo e incidentes TI</td></tr>
            <tr><td style="height:4px;background:{color}"></td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 16px;font-size:17px;font-weight:600">{WebUtility.HtmlEncode(c.Titulo)}</p>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{filas}</table>
              {boton}
              <p style="margin:20px 0 0;font-size:12px;color:#5F6B80">Recibes este aviso por tu nivel en Monitoreo e incidentes TI.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"{c.Titulo}\n\n{string.Join("\n", c.Filas.Select(f => $"{f.Etiqueta}: {f.Valor}"))}{(c.Enlace is null ? "" : $"\n\n{c.Enlace}")}";
        return (c.Asunto, html, texto);
    }
}
