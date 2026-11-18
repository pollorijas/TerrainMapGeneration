# TerrainMapGeneration — Investigación y planificación

> Estado: **aprobado e implementado como v0.1.0** (2026-11-18). Ver §5 para los cambios respecto a
> lo aprobado.

Herramienta propia, gratuita y sencilla para convertir un *heightmap* en escala de grises en un
mapa de terreno utilizable en un juego 2D de vista cenital (estilo Pokémon): erosión, capas de
superficie (arena, pasto, roca, nieve…), mapas RGB de capas y exportación por tiles.

---

## 0. Suposiciones (sobre lo que no se especificó)

| # | Suposición | Por qué |
|---|------------|---------|
| S1 | La herramienta es **2D** (procesa imágenes); no necesita vista 3D en la v1. | El juego objetivo es de vista cenital con tiles. 3D es un "nice to have". |
| S2 | La v1 es una **aplicación de consola (CLI)** configurada con un archivo JSON; la GUI llega en la v2. | Separa algoritmos de interfaz, permite pruebas automáticas y evita sobrediseñar. |
| S3 | Tamaños típicos de heightmap: 256² – 2048² píxeles; se procesa en **CPU**. | A esos tamaños la erosión por partículas en CPU tarda segundos, no requiere GPU. |
| S4 | Alturas internas normalizadas a `float` en `[0, 1]`. 1 píxel = 1 unidad horizontal; `heightScale` convierte la altura a las mismas unidades para calcular pendientes reales. | Hace los parámetros independientes del formato de la imagen (8 o 16 bits). |
| S5 | Tile del juego = **16×16 px** por defecto (configurable). Cada tile tiene **un solo tipo de superficie** (el dominante). | Convención de los juegos tipo Pokémon de GBA/DS. |
| S6 | Resultados **deterministas**: misma entrada + misma semilla ⇒ misma salida. | Indispensable para pruebas unitarias y para repetir un mapa. |
| S7 | Uso personal / no comercial (afecta a la licencia de ImageSharp, ver §1.4). | Lo indica "sin costo". |

---

## 1. Investigación

### 1.1 Erosión hidráulica

| Método | Idea | Ventajas | Desventajas |
|--------|------|----------|-------------|
| **Por partículas ("gotas")** — Hans T. Beyer (2015), popularizado por S. Lague | Se sueltan N gotas en posiciones aleatorias. Cada gota baja siguiendo el gradiente (interpolado bilinealmente) con inercia, carga sedimento según su capacidad `C = max(-Δh, minSlope) · velocidad · agua · k` y lo deposita cuando va cuesta arriba o se satura; el agua se evapora. | Sencilla (≈150 líneas), buena calidad visual (cauces, abanicos), determinista con semilla, bien en CPU, genera de paso **mapas de erosión y depósito** útiles para las capas. | Secuencial por naturaleza (paralelizar exige cuidado); no simula agua estancada (lagos). |
| **Por rejilla / *shallow water*** — Mei, Decaudin & Hu (2007); Šťava et al. (2008) | Cada celda guarda agua, sedimento y flujo hacia sus 4 vecinos ("pipe model"); se integra en pasos de tiempo. | Físicamente más plausible, forma lagos y ríos persistentes, paraleliza perfectamente (GPU). | Más parámetros y estado, inestable si el paso de tiempo es grande, lento en CPU para mapas grandes. |
| **Por acumulación de flujo / *stream power*** (geomorfología) | Calcula cuánta área drena por cada celda y erosiona ∝ área^m · pendiente^n. | Produce redes fluviales muy realistas a gran escala. | Requiere ordenar celdas / rellenar depresiones; más complejo de lo necesario. |

**Decisión v1:** erosión **por partículas (Beyer)**. Mejor relación calidad/complejidad y da
mapas de erosión/depósito gratis. La de rejilla queda como posible v3 (GPU).

### 1.2 Erosión térmica

Musgrave, Kolb & Mace (1989); variante optimizada de Olsen (2004). Simula material que se
desmorona cuando la pendiente supera el **ángulo de reposo (talud)**:

```
para cada celda c y cada vecino n:
    d = h(c) - h(n)
    si d > T:                      # T = tan(ánguloTalud) · distancia / heightScale
        mover  k · (d - T)  de c a n   (repartido proporcionalmente entre vecinos que exceden T)
```

- Simple, estable, **conserva la masa** (lo que sale de una celda entra en otra) ⇒ muy fácil de probar.
- Se usa doble buffer para que el resultado no dependa del orden de recorrido.
- Suaviza acantilados irreales y forma pedregales (*talus*) al pie de las laderas.

**Decisión v1:** implementarla (8 vecinos, doble buffer). Orden recomendado del pipeline:
térmica → hidráulica → térmica ligera opcional.

### 1.3 Máscaras por pendiente, altitud y erosión

1. **Pendiente**: diferencias centrales `dx = (h[x+1]-h[x-1])/2`, `dy` análogo;
   `pendiente° = atan(√(dx²+dy²) · heightScale)`. (Sobel es una alternativa más suave al ruido.)
2. **Altitud**: altura normalizada `[0,1]` (o en metros si se configura `heightScale`).
3. **Erosión / depósito**: acumulados por la erosión hidráulica (dónde se quitó o se dejó material).
4. **Regla de capa** = producto de funciones de pertenencia con transiciones suaves:
   `w = smoothRange(altitud, min, max, falloff) · smoothRange(pendiente, …) · f(erosión) · peso`
   donde `smoothRange` usa `smoothstep` en los bordes para evitar cortes duros.
5. **Normalización**: en cada píxel `w_i ← w_i / Σw`; si `Σw = 0`, se asigna una capa por defecto.
   Así las máscaras siempre suman 1 (requisito de un *splatmap*).
6. (Opcional) Un poco de ruido sobre los umbrales rompe las "bandas" de altitud demasiado rectas.

Ejemplo de reglas por defecto: *agua* < 0.20 de altitud · *arena* 0.20–0.26 y poca pendiente ·
*pasto* 0.26–0.70 con pendiente < 30° · *roca* pendiente > 35° o zonas muy erosionadas ·
*nieve* > 0.80 con pendiente < 45°.

### 1.4 Codificación de capas en canales RGB(A)

| Esquema | Descripción | Uso |
|---------|-------------|-----|
| **Splatmap RGBA** | Un canal por capa (R=arena, G=pasto, B=roca, A=nieve), valor = peso 0–255. Más de 4 capas ⇒ varios splatmaps. | Estándar en motores (Unity, Godot, MonoGame con shader). Mezcla suave. |
| **Máscaras individuales** | Una imagen en gris por capa. | Edición manual en GIMP/Krita; sin límite de capas. |
| **Mapa de índices / tipo** | Cada píxel o tile guarda el **ID** de la capa dominante, pintado con un color de paleta. | Lo que necesita un juego por tiles: un tile = un tipo. |
| **Mapa de color (preview)** | Mezcla de los colores de cada capa por su peso. | Visualizar el resultado. |

Advertencia práctica: en PNG, un canal **alfa** usado como dato puede ser alterado por editores que
pre-multiplican alfa; por eso la v1 exporta el splatmap RGBA **y** las máscaras individuales.
Para heightmaps se recomiendan **PNG de 16 bits**: con 8 bits solo hay 256 niveles y la erosión
produce "terrazas".

### 1.5 Tecnologías en .NET (hoy)

**Plataforma:** **.NET 10 (LTS, soporte hasta nov-2028)**. .NET 11 (STS) no aporta nada necesario aquí.

**Procesamiento de imágenes**

| Biblioteca | Ventajas | Desventajas |
|------------|----------|-------------|
| **SixLabors.ImageSharp 3.x** | 100 % administrada (sin binarios nativos), multiplataforma, API clara, soporta **L16 (gris 16 bits)** y RGBA64. | Licencia *Six Labors Split License*: gratis para código abierto, uso personal y empresas con < 1 M USD de ingresos; si no, de pago. |
| **SkiaSharp** | MIT, muy rápida, también sirve para dibujar (útil en una GUI). | Depende de binarios nativos; manejo de gris 16 bits más incómodo. |
| **StbImageSharp / StbImageWriteSharp** | Dominio público, diminutas. | API mínima; lectura de 16 bits limitada, sin operaciones de imagen. |
| **Magick.NET** | Lee casi cualquier formato (TIFF, EXR, DEM…). | Pesada (ImageMagick nativo), excesiva para este caso. |
| System.Drawing.Common | Conocida. | **Solo Windows** desde .NET 6. Descartada. |

**Decisión inicial:** ImageSharp, aislada en `TerrainMapGeneration.Imaging`.
**Decisión final (ver §5):** códec PNG propio dentro de `Imaging`, sin dependencias externas.

**Interfaz gráfica (v2)**

| Opción | Ventajas | Desventajas |
|--------|----------|-------------|
| **Avalonia UI** | MIT, Windows/Linux/macOS, XAML + MVVM (similar a WPF). | Curva de aprendizaje de XAML/MVVM. |
| WPF | Madura, gran ecosistema. | Solo Windows. |
| WinForms | La más rápida de aprender. | Solo Windows, aspecto anticuado, poco flexible. |
| ImGui.NET + Silk.NET/Veldrid | Ideal para herramientas tipo editor, vista 3D fácil. | Más "a mano"; interfaz inmediata no nativa. |
| MonoGame (+ ImGui) | Mismo ecosistema que el juego; vista 3D del terreno. | No es un framework de UI: formularios y diálogos cuestan mucho. |
| .NET MAUI | Oficial de Microsoft. | Soporte de escritorio Linux inexistente; orientada a móvil. |

**Decisión:** v1 sin GUI (CLI). v2 con **Avalonia** (vista previa 2D de capas y parámetros).

**Reemplazo de XNA**

XNA está abandonado desde 2013 (solo .NET Framework / Windows). Sus sucesores:

- **MonoGame 3.8.x** — continuación directa de la API de XNA, MIT, multiplataforma, .NET moderno.
  **Recomendado para el futuro juego**; los mapas de esta herramienta (PNG + JSON/CSV/Tiled) se
  cargan directamente con él.
- **FNA** — reimplementación exacta de XNA 4.0; ideal para *portar código XNA antiguo* sin cambios.
- Alternativas no-XNA: Raylib-cs, Godot (C#).

Para la herramienta en sí **no hace falta** ningún framework de juegos.

**Otras dependencias**

- `System.Text.Json` (incluida en .NET) para el archivo de proyecto.
- Pruebas: **xUnit** (estándar de facto en .NET, se integra con `dotnet test` y con todos los IDE).
- Argumentos de CLI: análisis manual simple en la v1 (2–3 opciones); `System.CommandLine` si crece.
- **Tiled Map Editor** (gratuito): su formato JSON (`.tmj`) es el destino natural para un juego por
  tiles; exportarlo es la primera ampliación tras la v1.

---

## 2. Planificación conceptual

### 2.1 Arquitectura

```
┌────────────────────────────┐
│ TerrainMapGeneration.Cli   │  ← lee proyecto JSON, orquesta, informa progreso
│ (v2: .App con Avalonia)    │
└─────────────┬──────────────┘
              │
┌─────────────▼──────────────┐   ┌──────────────────────────────┐
│ TerrainMapGeneration.Core  │   │ TerrainMapGeneration.Imaging │
│ (sin dependencias externas)│◄──┤ (códec PNG: PNG ⇄ FloatMap)  │
│ FloatMap, erosión, análisis│   └──────────────────────────────┘
│ capas, máscaras, tiles     │
└────────────────────────────┘
```

Regla clave: **Core no conoce imágenes ni archivos**, solo arreglos de `float`. Todo es testeable
con mapas sintéticos generados en memoria.

### 2.2 Módulos

| Módulo | Responsabilidad |
|--------|-----------------|
| `Maps` | `FloatMap`: rejilla `float[]` con ancho/alto, acceso con límites, muestreo bilineal, normalizar. |
| `Erosion` | `ThermalErosion`, `HydraulicErosion` (+ ajustes, + mapas de erosión/depósito). |
| `Analysis` | Pendiente (grados) y, más tarde, curvatura/flujo. |
| `Layers` | `SurfaceLayer` (reglas), `MaskGenerator` (pesos normalizados). |
| `Export` (Core) | `SplatmapEncoder` (capas → canales), `TileClassifier` (máscaras → IDs por tile), mezcla de color. |
| `Imaging` | Cargar heightmap (8/16 bits, RGB → luminancia), guardar gris/RGBA/índices. |
| `Pipeline` | `TerrainProject` (config JSON) + `TerrainPipeline` (ejecuta los pasos). |

### 2.3 Flujo de datos

```
heightmap.png ──► FloatMap [0..1]
                    │
                    ├─► ThermalErosion ──► HydraulicErosion ──► (heightmap erosionado)
                    │                          └──► erosión / depósito (FloatMap)
                    ▼
               Analysis: pendiente
                    ▼
     MaskGenerator(altitud, pendiente, erosión, reglas) ──► N máscaras que suman 1
                    ▼
  ┌─────────────────┼──────────────────┬───────────────────────┐
  ▼                 ▼                  ▼                       ▼
heightmap_eroded  splatmap_0.png    mask_<capa>.png      TileClassifier (16×16)
  .png (16 bits)  (RGBA = 4 capas)  (gris por capa)       ├─► tiles.png  (color por tipo)
                  colormap.png                            ├─► tiles.csv  (IDs)
                  (preview)                               └─► tiles.json (leyenda id→capa)
```

### 2.4 Alcance de la versión mínima viable (v0.1)

**Incluye**
1. Cargar PNG en gris (8 o 16 bits; si es RGB se convierte a luminancia).
2. Exportar un mapa RGB básico (coloreado por altura) — *primer hito*.
3. Erosión térmica y erosión hidráulica por partículas, configurables y deterministas.
4. Cálculo de pendiente y máscaras de 4–5 capas por altitud + pendiente + erosión.
5. Exportar: heightmap erosionado (PNG 16 bits), máscaras, splatmap RGBA, preview de color,
   mapa de tiles (PNG + CSV + leyenda JSON).
6. CLI: `terrain run proyecto.json` y `terrain init` (crea un JSON de ejemplo).
7. Pruebas unitarias de erosión, pendiente, máscaras y clasificación de tiles.

**Fuera de la v1** (ver hoja de ruta): GUI, vista 3D, exportación a Tiled, autotiling de
bordes (transiciones pasto↔arena), ríos/lagos explícitos, GPU, decoración (árboles, hierba alta).

### 2.5 Hoja de ruta posterior

- **v0.2** Exportación a Tiled (`.tmj` + tileset), soporte de agua por nivel del mar.
- **v0.3** GUI Avalonia con vista previa en vivo y edición de reglas.
- **v0.4** Autotiling (bordes de 47 tiles / *Wang tiles*), objetos dispersos (árboles, rocas,
  hierba alta con densidad por máscara).
- **v0.5+** Erosión de rejilla, vista 3D (MonoGame/Silk.NET), paralelización.

---

## 3. Planificación de programación

### 3.1 Estructura de carpetas

```
TerrainMapGeneration/
├─ .gitignore  .gitattributes  .editorconfig
├─ Directory.Build.props          # net10.0, nullable, warnings como errores
├─ TerrainMapGeneration.sln
├─ README.md   LICENSE   CHANGELOG.md
├─ docs/
│  └─ PLAN.md                      # este documento
├─ samples/
│  ├─ heightmaps/                  # heightmaps de ejemplo pequeños (≤ 512²)
│  └─ projects/default.json        # proyecto de ejemplo
├─ src/
│  ├─ TerrainMapGeneration.Core/
│  │  ├─ Maps/FloatMap.cs
│  │  ├─ Erosion/IHeightMapFilter.cs
│  │  ├─ Erosion/ThermalErosion.cs  ThermalErosionSettings.cs
│  │  ├─ Erosion/HydraulicErosion.cs  HydraulicErosionSettings.cs
│  │  ├─ Analysis/TerrainAnalysis.cs
│  │  ├─ Layers/SurfaceLayer.cs  LayerRule.cs  MaskGenerator.cs  LayerMasks.cs
│  │  ├─ Export/SplatmapEncoder.cs  TileClassifier.cs  ColorBlender.cs  Rgba.cs
│  │  └─ Pipeline/TerrainProject.cs  TerrainPipeline.cs  PipelineResult.cs
│  ├─ TerrainMapGeneration.Imaging/
│  │  ├─ HeightMapReader.cs
│  │  └─ ImageWriter.cs
│  └─ TerrainMapGeneration.Cli/
│     └─ Program.cs
└─ tests/
   ├─ TerrainMapGeneration.Core.Tests/
   │  ├─ TestMaps.cs               # generadores sintéticos: plano, rampa, cono, pico
   │  ├─ FloatMapTests.cs  TerrainAnalysisTests.cs
   │  ├─ ThermalErosionTests.cs  HydraulicErosionTests.cs
   │  ├─ MaskGeneratorTests.cs  TileClassifierTests.cs  SplatmapEncoderTests.cs
   └─ TerrainMapGeneration.Imaging.Tests/
      └─ HeightMapRoundTripTests.cs
```

### 3.2 Clases e interfaces principales

```csharp
public sealed class FloatMap                       // Core/Maps
{
    public int Width { get; }  public int Height { get; }
    public float[] Data { get; }                   // fila mayor: index = y * Width + x
    public float this[int x, int y] { get; set; }
    public float GetClamped(int x, int y);
    public float SampleBilinear(float x, float y);
    public FloatMap Clone();
    public (float Min, float Max) GetRange();
    public void Normalize();                        // reescala a [0,1]
}

public interface IHeightMapFilter { void Apply(FloatMap heightMap); }

public sealed record ThermalErosionSettings(int Iterations = 50, float TalusAngleDegrees = 35,
    float Strength = 0.5f, float HeightScale = 100);
public sealed class ThermalErosion(ThermalErosionSettings s) : IHeightMapFilter { … }

public sealed record HydraulicErosionSettings(int Droplets = 50_000, int Seed = 0,
    int BrushRadius = 3, float Inertia = 0.05f, float SedimentCapacity = 4f,
    float MinSlope = 0.01f, float ErodeSpeed = 0.3f, float DepositSpeed = 0.3f,
    float EvaporateSpeed = 0.01f, float Gravity = 4f, int MaxLifetime = 30);
public sealed class HydraulicErosion(HydraulicErosionSettings s) : IHeightMapFilter
{
    public FloatMap? ErosionMap { get; }           // material retirado por celda
    public FloatMap? DepositionMap { get; }        // material depositado por celda
}

public static class TerrainAnalysis { public static FloatMap SlopeDegrees(FloatMap h, float heightScale); }

public sealed record RangeRule(float Min, float Max, float Falloff);   // smoothstep en bordes
public sealed record SurfaceLayer(string Name, Rgba Color, RangeRule? Altitude,
    RangeRule? Slope, float ErosionInfluence = 0, float Weight = 1);
public sealed class LayerMasks { IReadOnlyList<SurfaceLayer> Layers; IReadOnlyList<FloatMap> Masks;
    int DominantLayerAt(int x, int y); }
public static class MaskGenerator { LayerMasks Generate(TerrainContext ctx, IReadOnlyList<SurfaceLayer> layers); }

public static class SplatmapEncoder { Rgba[][] Encode(LayerMasks m); }       // 4 capas por imagen
public static class TileClassifier  { int[,] Classify(LayerMasks m, int tileSize); } // mayoría ponderada
public static class ColorBlender    { Rgba[] Blend(LayerMasks m); }

public sealed class TerrainProject { /* rutas, heightScale, ajustes de erosión, capas, tileSize, salidas */ }
public sealed class TerrainPipeline { PipelineResult Run(FloatMap input, TerrainProject p); }
```

`TerrainContext` agrupa altura, pendiente y erosión para no pasar 4 parámetros sueltos.

### 3.3 Lista ordenada de tareas (cada una = 1 commit o PR pequeño)

| # | Tarea | Entregable verificable |
|---|-------|------------------------|
| 1 | Esqueleto: `.gitignore`, `.gitattributes`, `.editorconfig`, `Directory.Build.props`, solución y 5 proyectos vacíos, README | `dotnet build` y `dotnet test` pasan |
| 2 | `FloatMap` + pruebas | Pruebas de indexación, límites, bilineal, normalización |
| 3 | `HeightMapReader` (8/16 bits, RGB→gris) + `ImageWriter` (gris 16 bits, RGBA) + prueba de ida y vuelta | PNG leído ⇒ escrito ⇒ leído = igual |
| 4 | **Hito 1** — CLI: cargar heightmap y exportar mapa RGB coloreado por altura (gradiente agua→arena→pasto→roca→nieve) | `terrain run` produce `colormap.png` |
| 5 | `TerrainAnalysis.SlopeDegrees` + pruebas | Rampa conocida ⇒ ángulo esperado |
| 6 | `SurfaceLayer`, `RangeRule`, `MaskGenerator` + pruebas | Máscaras suman 1, reglas correctas |
| 7 | `ThermalErosion` + pruebas | Conservación de masa, talud respetado |
| 8 | `HydraulicErosion` + pruebas | Determinismo, depósitos al pie de la pendiente |
| 9 | `SplatmapEncoder`, `ColorBlender`, `TileClassifier` + pruebas | Salidas con dimensiones y valores correctos |
| 10 | `TerrainProject` (JSON) + `TerrainPipeline` + `terrain init` | Ejecución completa con `samples/projects/default.json` |
| 11 | Documentación (README con ejemplos e imágenes), CHANGELOG, etiqueta `v0.1.0` | |
| 12 | (Opcional) GitHub Actions: `dotnet build` + `dotnet test` en cada push | Insignia verde |

### 3.4 Criterios de prueba

**Erosión térmica**
- Mapa plano ⇒ sin cambios.
- Masa conservada: `|Σh_antes − Σh_después| < 1e-3 · Σh` (sin pérdidas por bordes).
- Tras suficientes iteraciones, ninguna diferencia entre vecinos supera el talud (+ tolerancia).
- Un pico aislado baja y sus vecinos suben.

**Erosión hidráulica**
- Misma semilla ⇒ resultado idéntico bit a bit; semilla distinta ⇒ resultado distinto.
- Mapa plano ⇒ cambios despreciables.
- En una rampa: la parte alta pierde material y la parte baja gana (depósito al pie).
- La altura máxima no aumenta; ningún valor es `NaN`/∞.
- `ErosionMap` y `DepositionMap` no negativos; su diferencia ≈ cambio total de altura.

**Pendiente**
- Plano ⇒ 0°. Rampa con `Δh/Δx · heightScale = 1` ⇒ 45° (± 0,5°).

**Máscaras**
- En cada píxel `Σ w_i = 1` (± 1e-5) y `0 ≤ w_i ≤ 1`.
- Píxel muy alto y plano ⇒ nieve dominante; pendiente > 45° ⇒ roca dominante; zona baja ⇒ agua.
- Transición suave: en el borde de un rango el peso está entre 0 y 1, no salta.
- Sin ninguna regla aplicable ⇒ capa por defecto.

**Exportación**
- Splatmap: con ≤ 4 capas, 1 imagen; con 5–8, 2 imágenes; canal = round(peso · 255).
- Tiles: mapa de 64×48 px con tile 16 ⇒ 4×3 tiles; tile homogéneo ⇒ su capa; mayoría ⇒ capa mayoritaria.
- Imaging: ida y vuelta 16 bits sin pérdida; 8 bits con error ≤ 1/255.

### 3.5 Control de versiones con Git

- Repositorio en GitHub (ya existe: `pollorijas/TerrainMapGeneration`); **nunca** trabajar sin `push`
  — es la copia de seguridad que te faltó la última vez.
- Ramas: `main` siempre compila y pasa pruebas; una rama por tarea (`feat/thermal-erosion`,
  `fix/…`), integradas mediante *pull request* (aunque trabajes solo: deja historial y ejecuta CI).
- Mensajes con *Conventional Commits*: `feat(core): add thermal erosion`, `test:`, `docs:`, `chore:`.
- `.gitignore` estándar de .NET (`bin/`, `obj/`, `.vs/`, `*.user`, `output/`).
- `.gitattributes`: `* text=auto eol=lf`, `*.png binary`.
- Imágenes de ejemplo pequeñas (< 1 MB) en el repo; si algún día hay muchas grandes, Git LFS.
- Etiquetas semánticas por versión: `v0.1.0`, `v0.2.0`…; `CHANGELOG.md` actualizado en cada una.
- Primer commit de código = tarea 1 (esqueleto que compila); este documento va antes, en `docs/`.

---

## 4. Requisitos previos

### 4.1 Herramientas

| Herramienta | Versión | Notas |
|-------------|---------|-------|
| **.NET SDK** | **10.0.x** (LTS) | `dotnet --version` debe mostrar `10.0.*`. |
| IDE (elige uno) | Visual Studio 2026 Community (Windows) · **JetBrains Rider** (gratis para uso no comercial) · VS Code + C# Dev Kit | Todos ejecutan las pruebas xUnit. |
| Git | 2.40 o superior | Configura `user.name`/`user.email` y autenticación con GitHub. |
| Editor de imágenes | **GIMP 2.10/3.x** o **Krita** | Ambos exportan PNG en gris de 16 bits. |
| (Opcional) Tiled | 1.11+ | Para la v0.2 (exportación de mapas por tiles). |

### 4.2 Datos de prueba que debes preparar

Las pruebas unitarias generan sus propios mapas sintéticos (plano, rampa, cono), así que **no
dependen de imágenes externas**. Para probar la herramienta a mano necesitas:

1. **Un heightmap real de 512×512, PNG gris de 16 bits.** Fuentes gratuitas:
   - Tangram Heightmapper (web) — recorta cualquier zona del mundo como heightmap.
   - Copernicus DEM GLO-30 u OpenTopography (datos reales; necesitan recorte/convertir).
   - Generado en GIMP: *Filtros → Renderizado → Ruido → Ruido sólido/Plasma*, luego exportar a 16 bits.
2. **Un heightmap de 8 bits** cualquiera (para comprobar la conversión y ver las "terrazas").
3. **Un heightmap dibujado a mano** pequeño (256×256) con una isla, una montaña y un valle: es
   el caso de uso real para diseñar un mapa de juego.
4. Decidir: **tamaño de tile** (16 px por defecto), **cuántos píxeles del heightmap equivalen a un
   tile** (por defecto 1 px = 1 px del mapa final, tile = 16×16 px) y **la lista de superficies**
   que tu juego necesita (por defecto: agua, arena, pasto, roca, nieve) con un color para cada una.

---

## 5. Cambios durante la implementación de la v0.1

| Cambio | Motivo |
|--------|--------|
| **ImageSharp → códec PNG propio** (`Imaging/Png`, solo `System.IO.Compression`). | ImageSharp 4.x exige una clave de licencia registrada para compilar en Release, y la última 3.x (3.1.12) tiene vulnerabilidades de severidad alta sin corregir. Un lector/escritor PNG es pequeño (≈570 líneas con comentarios), se prueba contra archivos de Pillow e ImageMagick y elimina la dependencia. Contrapartida: solo se admite PNG (otros formatos se convierten antes con GIMP/Krita). |
| `heightScale` por defecto **32** (no 64). | Con 64 la pendiente mediana de la isla de ejemplo era 36°, y casi todo quedaba como roca. |
| 6 capas por defecto: se añade **tierra** (zonas muy erosionadas). | Para que la erosión influya visiblemente en las capas, como pide el requisito. |
| `maskSmoothingRadius` suaviza también la pendiente. | Los surcos de un píxel de la erosión hidráulica moteaban las máscaras. |
| Bordes de `RangeRule` **centrados**. | Dos capas con un borde común y el mismo `falloff` suman exactamente 1: sin huecos ni solapes. |
| `HeightScale` sale de `ThermalErosionSettings`; se pasa al constructor de `ThermalErosion`. | Es una propiedad del terreno, no de la erosión; así no se repite en el JSON. |
| Comando extra `terrain colorize`. | Fue el primer hito (cargar heightmap y exportar un RGB básico); se conserva por utilidad. |
| Erosión hidráulica: la velocidad crece al bajar y las gotas que mueren dentro del mapa depositan su carga. | La referencia tiene el signo de la velocidad invertido y "pierde" sedimento; así la masa se conserva salvo lo que sale por los bordes. |
| Todo el trabajo va en una rama de sesión con un commit por tarea, en lugar de una rama por tarea. | Restricción del entorno donde se desarrolló; el historial sigue siendo de un commit por tarea. |

