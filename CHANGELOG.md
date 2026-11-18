# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versionado semántico.

## [0.1.0] - 2026-11-18

Primera versión mínima viable.

### Añadido
- `FloatMap`: rejilla de valores con muestreo bilineal, normalización y utilidades.
- Códec PNG propio sin dependencias: lee todos los tipos de color y profundidades (1–16 bits),
  paletas y entrelazado Adam7, con verificación de CRC; escribe gris/RGB(A) a 8 o 16 bits.
- Erosión térmica (ángulo de reposo, 8 vecinos, conserva la masa).
- Erosión hidráulica por partículas (Beyer 2015) con mapas de erosión y depósito.
- Pendiente en grados, hillshade, suavizado y normalización por percentil.
- Capas de superficie con reglas de rango suaves y máscaras normalizadas; 6 capas por defecto
  (agua, arena, pasto, tierra, roca, nieve).
- Exportación: heightmap erosionado (16 bits), mapa coloreado por altura, vista previa sombreada,
  máscaras por capa, splatmaps RGBA (+ leyenda JSON) y mapa de tiles (PNG, CSV y leyenda JSON).
- Proyecto JSON (`terrain init`) y ejecución completa (`terrain run`).
- Heightmap y proyecto de ejemplo; integración continua en GitHub Actions.
