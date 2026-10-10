import { useEffect, useMemo, useRef, useState } from "react";
import * as echarts from "echarts/core";
import { BarChart } from "echarts/charts";
import { GridComponent, LegendComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import s from "./analitica.module.css";

echarts.use([BarChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);

export interface Serie {
  nombre: string;
  valores: number[];
  /** Token de color de la paleta (ej. "--accent"). */
  color: string;
}

function leer(tokens: string[]) {
  const css = getComputedStyle(document.documentElement);
  return Object.fromEntries(tokens.map((t) => [t, css.getPropertyValue(t).trim()]));
}

/** Barras agrupadas con los colores del tema (se actualiza al cambiar entre claro y oscuro). */
export function Barras({ categorias, series, alto = 240 }: { categorias: string[]; series: Serie[]; alto?: number }) {
  const contenedor = useRef<HTMLDivElement>(null);
  const instancia = useRef<echarts.EChartsType | null>(null);
  const tokens = useMemo(() => [...series.map((x) => x.color), "--text-muted", "--border", "--surface"], [series]);
  const [c, setC] = useState(() => leer(tokens));

  useEffect(() => {
    const o = new MutationObserver(() => setC(leer(tokens)));
    o.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    return () => o.disconnect();
  }, [tokens]);

  useEffect(() => {
    if (!contenedor.current) return;
    const g = echarts.init(contenedor.current, undefined, { renderer: "canvas" });
    instancia.current = g;
    const r = new ResizeObserver(() => g.resize());
    r.observe(contenedor.current);
    return () => {
      r.disconnect();
      g.dispose();
      instancia.current = null;
    };
  }, []);

  useEffect(() => {
    instancia.current?.setOption({
      animation: false,
      grid: { left: 4, right: 8, top: 32, bottom: 4, containLabel: true },
      legend: { top: 0, right: 0, textStyle: { color: c["--text-muted"], fontSize: 12 }, itemWidth: 12, itemHeight: 8 },
      tooltip: { trigger: "axis", backgroundColor: c["--surface"], borderColor: c["--border"], textStyle: { color: c["--text-muted"], fontSize: 12 } },
      xAxis: { type: "category", data: categorias, axisLine: { lineStyle: { color: c["--border"] } }, axisLabel: { color: c["--text-muted"], fontSize: 11, hideOverlap: true } },
      yAxis: { type: "value", minInterval: 1, splitNumber: 3, axisLabel: { color: c["--text-muted"], fontSize: 11 }, splitLine: { lineStyle: { color: c["--border"], type: "dashed" } } },
      series: series.map((x) => ({ name: x.nombre, type: "bar", data: x.valores, barMaxWidth: 18, itemStyle: { color: c[x.color], borderRadius: [3, 3, 0, 0] } })),
    }, { notMerge: true });
  }, [categorias, series, c]);

  return <div ref={contenedor} className={s.lienzo} style={{ height: alto }} />;
}
