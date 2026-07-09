# R2Beat MD9 Importer para Unity

Importador de los formatos de R2Beat — **.md9** (modelo), **.ani** (animación) y **.dds** (texturas) — para Unity 6 (también funciona en Unity 2021+). Basado en el parser de la herramienta web R2Tools (md9tool), con la misma lógica byte a byte.

## Instalación

1. Copia la carpeta `R2BeatImporter` completa dentro de la carpeta `Assets` de tu proyecto Unity.
2. Espera a que compile (aparecerá el menú `Assets > R2Beat`).

## Uso

1. Copia tus archivos a una carpeta dentro de `Assets`, **manteniéndolos juntos** (igual que en el juego):

   ```
   Assets/Mapas/weimarpark_winter/
       front.md9
       front.ani
       nocull_bakery.dds
       nocull_bear_blue.dds
       ...
   ```

2. Unity importará el `.md9` automáticamente y generará un prefab con:
   - La jerarquía completa de partes/huesos (285 partes en el caso de `front.md9`), cada una con su transform local (posición/rotación/escala de la matriz MD9).
   - Un `Mesh` por parte, con vértices, normales, UVs e índices.
   - Un `Material` por material MD9 (URP Unlit por defecto, doble cara y manejo de alpha, igual que el visor web).
   - Las texturas `.dds` decodificadas (DXT1/DXT3/DXT5 y 32 bpp sin comprimir) como sub-assets.
   - Un `AnimationClip` por cada `.ani` de la carpeta, a 90 fps con interpolación lineal (idéntico a la herramienta web).

3. Arrastra el prefab a la escena. Con la opción Legacy activada (por defecto), el clip se reproduce solo al entrar en **Play**.

Si editas el `.ani` o un `.dds` fuera de Unity (por ejemplo con R2Tools web) y lo reemplazas, el `.md9` se **reimporta automáticamente** (dependencias registradas).

## Opciones del importador

Selecciona el `.md9` en el Project y ajusta en el Inspector:

| Opción | Descripción |
|---|---|
| **Escala** | Factor aplicado a la raíz. Los mapas usan unidades grandes (front.md9 mide ~34 700 unidades); prueba `0.01` si te resulta enorme en metros de Unity. |
| **Shader** | `UnlitAutomatico` (como el visor web) o `LitAutomatico` (con luces). Usa URP si está disponible; si no, Standard. |
| **Comprimir texturas** | Comprime al importar: ASTC 6x6 en Android/iOS, DXT en escritorio. Desactivado por defecto (RGBA32). |
| **Buscar ani** | `TodosEnLaCarpeta` (por defecto) o `SoloMismoNombreBase` (solo `front.ani` para `front.md9`). |
| **Animación Legacy** | Clips Legacy + componente `Animation` (reproducción directa). Desactívalo para usar `Animator` con tu propio AnimatorController. |
| **Animación en loop / Reproducir al iniciar** | Comportamiento de reproducción. |

Después de cambiar opciones pulsa **Apply** para reimportar.

## Notas para el build móvil

- Las texturas DDS se decodifican a RGBA32 dentro del asset. Para el build final conviene activar **Comprimir texturas** (ASTC en móvil), o convertir los DDS a PNG con el menú `Assets > R2Beat > Convertir DDS seleccionados a PNG` y borrar los `.dds` (el importador encuentra el PNG por nombre base automáticamente, igual que la web). Con PNG, Unity aplica toda su tubería normal de compresión por plataforma.
- Los materiales se crean con sombras desactivadas y sin iluminación (Unlit), como en el juego original. Cámbialo con la opción **Shader** si lo necesitas.

## Detalles técnicos (por si tocas el código)

- **Coordenadas**: MD9 usa la convención left-handed Y-up de Direct3D, la **misma de Unity**, así que posiciones, normales, matrices y cuaterniones se usan tal cual. (La herramienta web invierte Z porque three.js es right-handed — eso aquí **no** se hace.) Lo único que se convierte es la **V de los UV** (`v' = 1 − v`) y el volteo vertical de las filas del DDS, porque D3D pone el origen de textura arriba y Unity abajo — ambos flips juntos dejan el texturizado idéntico.
- **Animación**: los tiempos del `.ani` están en frames a 90 fps (`ANIMATION_FPS` de la web); se convierten a segundos (`t/90`) y `frameRate = 90`. Curvas con tangentes lineales para replicar la interpolación lerp/slerp del visor.
- **Jerarquía**: `parentId` de cada parte; las pistas del `.ani` se asocian por **nombre de parte** (si hay nombres duplicados, gana el último, igual que la web).
- **Formatos MD9**: se soportan el formato viejo y el nuevo (cabecera `-1` + 16 bytes extra por material).
- La lógica de parseo fue verificada byte a byte contra el parser JavaScript original de R2Tools.

## Archivos

```
R2BeatImporter/
└── Editor/
    ├── Md9Model.cs        # Estructuras de datos MD9/ANI
    ├── Md9Parser.cs       # Parser binario .md9
    ├── AniParser.cs       # Parser binario .ani
    ├── DdsDecoder.cs      # Decodificador DDS (DXT1/3/5, 32bpp)
    ├── Md9Importer.cs     # ScriptedImporter: .md9 → prefab + clips + texturas
    ├── R2BeatMenuTools.cs # Menú: convertir DDS a PNG
    └── R2Beat.Importer.Editor.asmdef
```

## Limitaciones actuales

- Solo importación (para exportar `.md9`/`.glb` sigue usándose R2Tools web, como acordamos).
- Los `.bin` de pistas (track bin) no se importan todavía — se puede añadir después con la misma base.
- Texturas TGA: se cargan solo si Unity puede decodificarlas por bytes; DDS de 24 bpp sin comprimir no está soportado (tampoco lo soporta la herramienta web).
