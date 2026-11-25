# Another Client

Distribución de Another Client para Windows x64.

Los paquetes completos y el instalador se distribuyen mediante **GitHub Releases**. El historial Git contiene el descargador, sus pruebas y los metadatos firmados del canal.

## Versión preparada

El paquete actual es **alpha 1.5**, sin cuentas ni sesiones incluidas. Descarga [AnotherClientSetup.exe](https://github.com/chuletas123/another-client/releases/download/alpha-1.5/AnotherClientSetup.exe) y ejecútalo con tu usuario habitual de Windows.

El instalador descarga el cliente completo, lo verifica y crea **Ac player** en el escritorio. Las siguientes aperturas desde ese acceso directo comprueban las actualizaciones antes de abrir el cliente.

## Instalador y actualizaciones

La instalación utiliza `%LOCALAPPDATA%\Another Client`, con versiones completas en `Versions`. Los datos personales permanecen fuera de esas versiones. No se cambian las carpetas ni los protocolos del Roblox oficial.

El canal [alpha](channels/alpha.json) está firmado y el instalador incluye su clave pública de verificación. Consulta [distribución y publicación](docs/distribucion.md) para preparar versiones posteriores.
