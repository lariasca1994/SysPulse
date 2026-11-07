import { test } from "node:test";
import assert from "node:assert/strict";
import { errorConfirmacion, esCodigoSeisDigitos, hayCodigosNuevos, puedeGuardarPassword, textoArchivoCodigos } from "./recuperacion.ts";

test("restablecer: solo pide contraseña nueva y confirmación", () => {
  assert.equal(puedeGuardarPassword("frase larga y segura 2026", "frase larga y segura 2026"), true);
  assert.equal(puedeGuardarPassword("", ""), false);
  assert.equal(puedeGuardarPassword("frase larga y segura 2026", ""), false);
});

test("restablecer: la confirmación distinta muestra el error", () => {
  assert.equal(errorConfirmacion("abc", "abd"), "Las contraseñas no coinciden.");
  assert.equal(errorConfirmacion("abc", "abc"), undefined);
  assert.equal(errorConfirmacion("abc", ""), undefined);
  assert.equal(puedeGuardarPassword("abc", "abd"), false);
});

test("perdí mi autenticador: el código del correo tiene 6 dígitos", () => {
  assert.equal(esCodigoSeisDigitos("123456"), true);
  assert.equal(esCodigoSeisDigitos(" 123456 "), true);
  assert.equal(esCodigoSeisDigitos("12345"), false);
  assert.equal(esCodigoSeisDigitos("12a456"), false);
  assert.equal(esCodigoSeisDigitos("ABCD-EFGH"), false);
});

test("códigos nuevos: solo se muestran si llegan en la respuesta", () => {
  assert.equal(hayCodigosNuevos(["ABCD-EFGH"]), true);
  assert.equal(hayCodigosNuevos([]), false);
  assert.equal(hayCodigosNuevos(null), false);
  assert.equal(hayCodigosNuevos(undefined), false);
});

test("códigos nuevos: el .txt lleva el nombre descriptivo y un código por línea", () => {
  const texto = textoArchivoCodigos(["ABCD-EFGH", "JKLM-NPQR"]);
  assert.match(texto, /Monitoreo e incidentes TI/);
  assert.match(texto, /\nABCD-EFGH\nJKLM-NPQR\n$/);
  assert.doesNotMatch(texto, /SysPulse/);
});
