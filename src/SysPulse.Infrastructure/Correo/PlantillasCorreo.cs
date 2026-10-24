using System.Net;

namespace SysPulse.Infrastructure.Correo;

/// <summary>
/// Plantillas HTML de correo: CSS en línea, una columna, sin JavaScript, sin fuentes ni imágenes remotas,
/// con versión en texto plano. Mismos colores de la plataforma (sección 7).
/// </summary>
public static class PlantillasCorreo
{
    public static (string Asunto, string Html, string Texto) VerificarCorreo(string nombre, string enlace)
    {
        const string Asunto = "Confirma tu correo en SysPulse";
        var n = WebUtility.HtmlEncode(nombre);
        var e = WebUtility.HtmlEncode(enlace);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>SysPulse</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 20px;font-size:14px;line-height:1.5;color:#4F5D73">Confirma tu correo para activar tu cuenta. El enlace vence en 24 horas.</p>
              <p style="margin:0 0 20px"><a href="{e}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">Confirmar correo</a></p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no creaste una cuenta en SysPulse, ignora este mensaje.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nConfirma tu correo para activar tu cuenta en SysPulse (vence en 24 horas):\n{enlace}\n\nSi no creaste una cuenta, ignora este mensaje.";
        return (Asunto, html, texto);
    }

    public static (string Asunto, string Html, string Texto) CodigoVerificacion(string nombre, string codigo)
    {
        var asunto = $"{codigo} es tu código de verificación de SysPulse";
        var n = WebUtility.HtmlEncode(nombre);
        var c = WebUtility.HtmlEncode(codigo);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>SysPulse</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 16px;font-size:14px;line-height:1.5;color:#4F5D73">Este es tu código de verificación en dos pasos. Vence en 10 minutos y sirve una sola vez.</p>
              <p style="margin:0 0 20px;font-size:32px;font-weight:700;letter-spacing:8px;color:#0B3A7E">{c}</p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no intentaste ingresar, cambia tu contraseña: alguien la conoce. Nunca te pediremos este código por otro medio.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nTu código de verificación de SysPulse es {codigo}. Vence en 10 minutos y sirve una sola vez.\n\nSi no intentaste ingresar, cambia tu contraseña.";
        return (asunto, html, texto);
    }

    public static (string Asunto, string Html, string Texto) RestablecerPassword(string nombre, string enlace)
    {
        const string Asunto = "Restablece tu contraseña de SysPulse";
        var n = WebUtility.HtmlEncode(nombre);
        var e = WebUtility.HtmlEncode(enlace);
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>SysPulse</td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 12px;font-size:16px">Hola, {n}:</p>
              <p style="margin:0 0 20px;font-size:14px;line-height:1.5;color:#4F5D73">Recibimos una solicitud para restablecer tu contraseña. El enlace vence en 1 hora y te pedirá también el código de tu app autenticadora.</p>
              <p style="margin:0 0 20px"><a href="{e}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">Restablecer contraseña</a></p>
              <p style="margin:0;font-size:12px;color:#5F6B80">Si no lo solicitaste, ignora este mensaje: tu contraseña no cambia.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"Hola, {nombre}:\n\nPara restablecer tu contraseña de SysPulse abre este enlace (vence en 1 hora; te pedirá el código de tu app autenticadora):\n{enlace}\n\nSi no lo solicitaste, ignora este mensaje.";
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
            <p style="margin:20px 0 0"><a href="{WebUtility.HtmlEncode(c.Enlace)}" style="display:inline-block;background:#0B3A7E;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600">{WebUtility.HtmlEncode(c.TextoEnlace ?? "Abrir en SysPulse")}</a></p>
            """;
        var html = $"""
            <!doctype html><html lang="es"><body style="margin:0;padding:24px 12px;background:#F4F6FA;font-family:'Segoe UI',Arial,sans-serif;color:#0B1F3A">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#FFFFFF;border:1px solid #DDE3EC;border-radius:12px">
            <tr><td style="background:#0B2A5B;border-radius:12px 12px 0 0;padding:18px 24px;color:#FFFFFF;font-size:18px;font-weight:600">
              <span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#E1251B;margin-right:8px"></span>SysPulse</td></tr>
            <tr><td style="height:4px;background:{color}"></td></tr>
            <tr><td style="padding:24px">
              <p style="margin:0 0 16px;font-size:17px;font-weight:600">{WebUtility.HtmlEncode(c.Titulo)}</p>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{filas}</table>
              {boton}
              <p style="margin:20px 0 0;font-size:12px;color:#5F6B80">Recibes este aviso por tu nivel en SysPulse.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        var texto = $"{c.Titulo}\n\n{string.Join("\n", c.Filas.Select(f => $"{f.Etiqueta}: {f.Valor}"))}{(c.Enlace is null ? "" : $"\n\n{c.Enlace}")}";
        return (c.Asunto, html, texto);
    }
}
