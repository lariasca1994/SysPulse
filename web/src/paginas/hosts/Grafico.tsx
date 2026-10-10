import { useEffect, useMemo, useRef, useState } from "react";
import * as echarts from "echarts/core";
import { LineChart } from "echarts/charts";
import { GridComponent, MarkLineComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { Punto, Umbral } from "../../api/hosts";
import s from "./detalle.module.css";

echarts.use([LineChart, GridComponent, TooltipComponent, MarkLineComponent, CanvasRenderer]);

interface Colores {
  linea: string;
  maximo: string;
  texto: string;
  borde: string;
  superficie: string;
  advertencia: string;
  excepcion: string;
}

/** Lee los tokens del tema actual y se actualiza al cambiar entre claro y oscuro. */
function useColores(): Colores {
  const leer = (): Colores => {
    const css = getComputedStyle(document.documentElement);
    const v = (n: string) => css.getPropertyValue(n).trim();
    return {
      linea: v("--accent"),
      maximo: v("--text-subtle"),
      texto: v("--text-muted"),
      borde: v("--border"),
      superficie: v("--surface"),
      advertencia: v("--warning-fill"),
      excepcion: v("--critical"),
    };
  };
  const [colores, setColores] = useState(leer);
  useEffect(() => {
    const observador = new MutationObserver(() => setColores(leer()));
    observador.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    return () => observador.disconnect();
  }, []);
  return colores;
}

const hora = new Intl.DateTimeFormat("es-CO", { hour: "2-digit", minute: "2-digit" });
const diaHora = new Intl.DateTimeFormat("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });

export function Grafico({
  puntos,
  formato,
  umbral,
  maximo100,
  horas,
}: {
  puntos: Punto[];
  formato: (v: number) => string;
  umbral?: Umbral;
  maximo100?: boolean;
  horas: number;
}) {
  const c = useColores();

  const opciones = useMemo(() => {
    const marcas = umbral
      ? [
          { yAxis: umbral.advertencia, lineStyle: { color: c.advertencia }, label: { show: false } },
          { yAxis: umbral.excepcion, lineStyle: { color: c.excepcion }, label: { show: false } },
        ]
      : [];
    return {
      animation: false,
      grid: { left: 8, right: 12, top: 12, bottom: 4, containLabel: true },
      tooltip: {
        trigger: "axis",
        backgroundColor: c.superficie,
        borderColor: c.borde,
        textStyle: { color: c.texto, fontSize: 12 },
        valueFormatter: (v: number) => formato(v),
      },
      xAxis: {
        type: "time",
        axisLine: { lineStyle: { color: c.borde } },
        axisLabel: { color: c.texto, fontSize: 11, hideOverlap: true, formatter: (v: number) => (horas > 24 ? diaHora : hora).format(v) },
        splitLine: { show: false },
      },
      yAxis: {
        type: "value",
        min: 0,
        max: maximo100 ? 100 : undefined,
        splitNumber: 3,
        axisLabel: { color: c.texto, fontSize: 11, formatter: (v: number) => formato(v) },
        splitLine: { lineStyle: { color: c.borde, type: "dashed" } },
      },
      series: [
        {
          name: "Promedio",
          type: "line",
          showSymbol: false,
          smooth: 0.2,
          data: puntos.map((p) => [p[0], p[1]]),
          lineStyle: { width: 2, color: c.linea },
          itemStyle: { color: c.linea },
          areaStyle: {
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
              { offset: 0, color: `${c.linea}55` },
              { offset: 1, color: `${c.linea}00` },
            ]),
          },
          markLine: marcas.length ? { silent: true, symbol: "none", lineStyle: { type: "dashed", width: 1 }, data: marcas } : undefined,
        },
        {
          name: "Máximo",
          type: "line",
          showSymbol: false,
          data: puntos.map((p) => [p[0], p[2]]),
          lineStyle: { width: 1, color: c.maximo, type: "dotted" },
          itemStyle: { color: c.maximo },
        },
      ],
    };
  }, [puntos, formato, umbral, maximo100, horas, c]);

  const contenedor = useRef<HTMLDivElement>(null);
  const instancia = useRef<echarts.EChartsType | null>(null);

  // Una instancia por gráfico; se redimensiona con su contenedor (menú colapsado, rotación del celular…).
  useEffect(() => {
    if (!contenedor.current) return;
    const grafico = echarts.init(contenedor.current, undefined, { renderer: "canvas" });
    instancia.current = grafico;
    const observador = new ResizeObserver(() => grafico.resize());
    observador.observe(contenedor.current);
    return () => {
      observador.disconnect();
      grafico.dispose();
      instancia.current = null;
    };
  }, []);

  useEffect(() => {
    instancia.current?.setOption(opciones, { notMerge: true });
  }, [opciones]);

  return <div ref={contenedor} className={s.lienzo} />;
}
