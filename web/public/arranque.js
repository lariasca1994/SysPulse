// Animación de entrada de la app instalada (PWA). Solo al abrir la app desde el ícono del teléfono o del escritorio
// (modo "standalone"), una vez por apertura, entre 1,1 y 1,6 segundos. Nunca bloquea: si algo falla no se muestra.
(function () {
  var el = document.getElementById("arranque");
  if (!el) return;
  var instalada = false;
  var visto = false;
  try {
    instalada = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
    visto = sessionStorage.getItem("arranque-visto") === "1";
  } catch (e) {
    instalada = false;
  }
  if (!instalada || visto) {
    el.parentNode.removeChild(el);
    return;
  }
  try {
    sessionStorage.setItem("arranque-visto", "1");
  } catch (e) {
    // Sin almacenamiento: se mostraría en cada carga, así que no se muestra.
    el.parentNode.removeChild(el);
    return;
  }

  var reducir = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var inicio = Date.now();
  var minimo = reducir ? 300 : 1100;
  var maximo = 1600;
  var cerrado = false;
  el.hidden = false;

  function cerrar() {
    if (cerrado) return;
    cerrado = true;
    el.className += " arranque-salida";
    window.setTimeout(function () {
      if (el.parentNode) el.parentNode.removeChild(el);
    }, 320);
  }

  function listo() {
    window.setTimeout(cerrar, Math.max(0, minimo - (Date.now() - inicio)));
  }

  if (document.readyState === "complete") listo();
  else window.addEventListener("load", listo, { once: true });
  window.setTimeout(cerrar, maximo);
})();
