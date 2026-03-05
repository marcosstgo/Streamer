# Streamer Pro

Streamer Pro es una aplicación de escritorio WPF para Windows que facilita la transmisión de vídeo usando `ffmpeg` como motor de codificación. Esta versión está preparada para .NET 8 y contiene mejoras de fiabilidad, manejo de procesos FFmpeg, minimizado a bandeja y registros de diagnóstico.

Resumen rápido
- Interfaz WPF para seleccionar fuentes online o carpetas locales (playlist).
- Soporta modos online y carpeta, con validación por `ffprobe`.
- Manejo robusto de procesos `ffmpeg` (Job object) para evitar procesos huérfanos.
- Minimizado a bandeja con limpieza de icono y manejadores.
- Registro de eventos y errores en `%AppData%\CorilloStreamer\streamer.log`.

Requisitos
- Windows 10/11 (x64)
- .NET 8 Runtime
- `ffmpeg.exe` y `ffprobe.exe` disponibles en el mismo directorio que el ejecutable de la aplicación o instalados en el PATH.

Licencia de FFmpeg
FFmpeg es software de terceros con su propia licencia (LGPL o GPL según opciones de compilación). Consulta `LICENSE-FFMPEG.txt` en este repositorio para detalles y enlaces.

Cómo obtener FFmpeg (binarios oficiales y builds comunes)
- Sitio oficial: https://ffmpeg.org/download.html
- Builds populares y confiables (estáticos) para Windows:
  - Gyan (builds estables): https://www.gyan.dev/ffmpeg/builds/
  - BtbN (builds modernos y automáticos): https://github.com/BtbN/FFmpeg-Builds/releases

Descargar y verificar checksums
Para seguridad e integridad, verifica las sumas SHA256 de los binarios antes de usarlos.

Checksums incluidos en esta release (presentes en `CHANGELOG.md`):
- ffmpeg.exe SHA256: `5AF82A0D4FE2B9EAE211B967332EA97EDFC51C6B328CA35B827E73EAC560DC0D`
- ffprobe.exe SHA256: `192A1D6899059765AC8C39764FC3148D4E6049955956DC2029F81F4BD6A8972D`

Comandos para verificar (ejemplos):

Windows (PowerShell):

```powershell
Get-FileHash -Algorithm SHA256 .\ffmpeg.exe
Get-FileHash -Algorithm SHA256 .\ffprobe.exe
```

Compare la salida `Hash` con las sumas indicadas arriba.

Linux / macOS:

```bash
sha256sum ffmpeg
sha256sum ffprobe
# o
shasum -a 256 ffmpeg
```

Qué hacer si la suma no coincide
- Si la suma no coincide, no uses esos binarios: podrían estar corruptos o ser una versión diferente.
- Descarga de nuevo desde la fuente oficial o utiliza otro proveedor confiable.

Distribución recomendada
- En lugar de mantener `ffmpeg.exe`/`ffprobe.exe` en el historial del repositorio, sube esos binarios como _assets_ en la página de GitHub Release y proporciona los checksums en el `CHANGELOG.md` y en este `README.md`.
- Alternativa: usar Git LFS para gestionar los binarios grandes.

Verificación automática (opcional)
Puedes crear un pequeño script PowerShell `tools\verify-ffmpeg.ps1` que calcule las sumas y las compare con los valores esperados. ¿Quieres que lo agregue?

Notas finales
- La presencia de `ffmpeg`/`ffprobe` en el mismo directorio que el ejecutable facilita su ejecución por parte de la aplicación.
- Asegúrate de cumplir las obligaciones de licencia al redistribuir binarios de FFmpeg (ver `LICENSE-FFMPEG.txt`).

Si quieres, puedo agregar el script de verificación y/o publicar instrucciones paso a paso para subir los binarios como assets en la Release de GitHub.
