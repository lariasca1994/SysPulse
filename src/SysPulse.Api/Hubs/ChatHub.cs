using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using SysPulse.Api.Seguridad;
using SysPulse.Api.Servicios;

namespace SysPulse.Api.Hubs;

/// <summary>
/// Chat en tiempo real (SignalR, /hubs/chat). Requiere sesión (el JWT viaja en access_token; el sello de seguridad
/// se valida igual que en la API). Límite de 20 mensajes por minuto por persona.
/// </summary>
[Authorize]
public sealed class ChatHub(ServicioChat chat) : Hub
{
    private const int MaxPorMinuto = 20;
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Envios = new(StringComparer.Ordinal);

    private PersonaConectada Yo => new(
        Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "",
        Context.User?.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Usuario",
        Context.User?.FindFirstValue(Tokens.ClaimRol) ?? "");

    public override async Task OnConnectedAsync()
    {
        await Clients.All.SendAsync("presencia", chat.Conectar(Yo));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("presencia", chat.Desconectar(Yo.Id));
        await base.OnDisconnectedAsync(exception);
    }

    public IReadOnlyList<PersonaConectada> Presentes() => chat.Presentes();

    public Task<IReadOnlyList<MensajeVista>> Mensajes(string salaId, string? antes) =>
        chat.MensajesAsync(salaId, antes, Context.ConnectionAborted);

    public Task<MensajeVista> Enviar(string salaId, string texto)
    {
        var yo = Yo;
        var cola = Envios.GetOrAdd(yo.Id, _ => new Queue<DateTime>());
        lock (cola)
        {
            var ahora = DateTime.UtcNow;
            while (cola.Count > 0 && ahora - cola.Peek() > TimeSpan.FromMinutes(1)) cola.Dequeue();
            if (cola.Count >= MaxPorMinuto) throw new HubException("Vas muy rápido: espera unos segundos antes de enviar otro mensaje.");
            cola.Enqueue(ahora);
        }
        return chat.EnviarAsync(salaId, yo, texto, Context.ConnectionAborted);
    }

    public Task Escribiendo(string salaId) =>
        Clients.Others.SendAsync("escribiendo", salaId, Yo.Nombre, Context.ConnectionAborted);

    public Task<MensajeVista> Retirar(string mensajeId) => chat.RetirarAsync(mensajeId, Yo.Id, Context.ConnectionAborted);

    public Task MarcarLeido(string salaId, string mensajeId) =>
        chat.MarcarLeidoAsync(salaId, ObjectId.Parse(Yo.Id), mensajeId, Context.ConnectionAborted);
}
