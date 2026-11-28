# Another Client

Distribución de Another Client para Windows x64.

Los paquetes completos y el instalador se distribuyen mediante **GitHub Releases**. El historial Git contiene el descargador, sus pruebas y los metadatos firmados del canal.

## Versión preparada

El paquete actual es **alpha 1.5.10**, sin cuentas ni sesiones incluidas. Descarga [AnotherClientSetup.exe](https://github.com/chuletas123/another-client/releases/download/alpha-1.5.10/AnotherClientSetup.exe) y ejecútalo con tu usuario habitual de Windows.

El instalador descarga el cliente completo, lo verifica y crea **Ac player** en el escritorio. Tanto ese acceso directo como el propio **Another Client.exe** comprueban las actualizaciones antes de cargar el cliente. Si hay una nueva versión, muestran **Updating Another Client...** durante la descarga, verificación e instalación y después abren la versión nueva.

## Instalador y actualizaciones

La instalación utiliza `%LOCALAPPDATA%\Another Client`, con versiones completas en `Versions`. Los datos personales permanecen fuera de esas versiones. No se cambian las carpetas ni los protocolos del Roblox oficial.

El canal [alpha](channels/alpha.json) está firmado y el instalador incluye su clave pública de verificación. Consulta [distribución y publicación](docs/distribucion.md) para preparar versiones posteriores.

La ventana de preparación usa un fondo sólido en Windows 10, sesiones remotas o equipos sin desenfoque compatible. En Windows 11 conserva el desenfoque cuando está disponible. La falta de APIs o la desactivación de transparencia no impide el arranque.
