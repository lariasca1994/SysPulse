using System.Globalization;
using global::Android.Content;
using global::Android.Provider;
using AndroidApp = global::Android.App.Application;
using AndroidUri = global::Android.Net.Uri;

namespace SysPulse.Agent.Android;

public partial class MainPage : ContentPage
{
	private IDispatcherTimer? _refresco;

	public MainPage()
	{
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		var mensaje = await ConfiguracionAndroid.ImportarSiExisteAsync();
		if (mensaje is not null) MensajeLabel.Text = mensaje;

		if (OperatingSystem.IsAndroidVersionAtLeast(33)) await Permissions.RequestAsync<Permissions.PostNotifications>();

		VincularPanel.IsVisible = !await ConfiguracionAndroid.EstaConfiguradoAsync();

		// Actualización obligatoria pendiente de confirmación: se muestra apenas se abre la app.
		ActualizadorAndroid.MostrarConfirmacionSiHay(Platform.CurrentActivity ?? AndroidApp.Context);

		_refresco = Dispatcher.CreateTimer();
		_refresco.Interval = TimeSpan.FromSeconds(2);
		_refresco.Tick += (_, _) => Refrescar();
		_refresco.Start();
		Refrescar();
	}

	protected override void OnDisappearing()
	{
		_refresco?.Stop();
		base.OnDisappearing();
	}

	private void Refrescar()
	{
		var motor = ServicioMonitoreo.Motor;
		var activo = motor is not null;
		EstadoLabel.Text = activo ? "Estado: monitoreando" : "Estado: detenido";
		MonitoreoButton.Text = activo ? "Detener monitoreo" : "Iniciar monitoreo";
		if (motor is null) return;

		var e = motor.Estado;
		HostLabel.Text = $"Host: {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}";
		CicloLabel.Text = $"Último ciclo: {Hora(e.UltimoCiclo)} ({e.LecturasUltimoCiclo} lecturas)";
		EnvioLabel.Text = $"Último envío a Atlas: {Hora(e.UltimoEnvioAtlas)}";
		BufferLabel.Text = e.Buffer;
		ErrorLabel.Text = e.UltimoError ?? "";
	}

	private static string Hora(DateTimeOffset? ts) =>
		ts?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";

	private async void OnMonitoreoClicked(object? sender, EventArgs e)
	{
		var contexto = AndroidApp.Context;
		if (ServicioMonitoreo.Motor is not null)
		{
			ConfiguracionAndroid.MonitoreoActivo = false;
			ServicioMonitoreo.Detener(contexto);
			return;
		}

		if (!await ConfiguracionAndroid.EstaConfiguradoAsync())
		{
			MensajeLabel.Text = "Primero vincula el celular con el código de alta.";
			VincularPanel.IsVisible = true;
			return;
		}
		ConfiguracionAndroid.MonitoreoActivo = true;
		ServicioMonitoreo.Iniciar(contexto);
	}

	private async void OnVincularClicked(object? sender, EventArgs e)
	{
		var codigo = (CodigoEntry.Text ?? "").Trim();
		if (codigo.Count(char.IsLetterOrDigit) != 8)
		{
			MensajeLabel.Text = "El código tiene 8 caracteres (ej. K7QM-4XPA).";
			return;
		}
		VincularButton.IsEnabled = false;
		MensajeLabel.Text = "Vinculando… (puede tardar hasta un minuto si el servidor estaba dormido)";
		var error = await ConfiguracionAndroid.VincularAsync(codigo);
		VincularButton.IsEnabled = true;
		if (error is not null)
		{
			MensajeLabel.Text = error;
			return;
		}
		VincularPanel.IsVisible = false;
		CodigoEntry.Text = "";
		MensajeLabel.Text = "Celular vinculado. El monitoreo ya está en marcha.";
		ConfiguracionAndroid.MonitoreoActivo = true;
		ServicioMonitoreo.Iniciar(AndroidApp.Context);
	}

	private void OnInicioAutomaticoClicked(object? sender, EventArgs e)
	{
		// Pantalla de inicio automático del fabricante, si existe; si no, los ajustes de la app.
		var intento = new Intent().SetComponent(new ComponentName("com.miui.securitycenter", "com.miui.permcenter.autostart.AutoStartManagementActivity"));
		if (!Abrir(intento)) OnAjustesAppClicked(sender, e);
	}

	private void OnBateriaClicked(object? sender, EventArgs e)
	{
		var intento = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, AndroidUri.Parse($"package:{AppInfo.Current.PackageName}"));
		if (!Abrir(intento)) OnAjustesAppClicked(sender, e);
	}

	private void OnAjustesAppClicked(object? sender, EventArgs e) =>
		Abrir(new Intent(Settings.ActionApplicationDetailsSettings, AndroidUri.Parse($"package:{AppInfo.Current.PackageName}")));

	private static bool Abrir(Intent intento)
	{
		try
		{
			intento.AddFlags(ActivityFlags.NewTask);
			AndroidApp.Context.StartActivity(intento);
			return true;
		}
		catch (ActivityNotFoundException)
		{
			return false;
		}
	}

	private async void OnPruebaClicked(object? sender, EventArgs e)
	{
		PruebaButton.IsEnabled = false;
		ResultadosLabel.Text = "Ejecutando…";
		try
		{
			var resultados = await Task.Run(PruebaTecnica.EjecutarAsync);
			var lineas = resultados.Select(r => $"{(r.Ok ? "OK   " : "FALLA")} {r.Prueba}: {r.Detalle}").ToList();
			ResultadosLabel.Text = string.Join(Environment.NewLine + Environment.NewLine, lineas);
			foreach (var linea in lineas) global::Android.Util.Log.Info("SysPulsePrueba", linea);
		}
		finally
		{
			PruebaButton.IsEnabled = true;
		}
	}
}
