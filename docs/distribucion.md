# Instalación y actualizaciones

El descargador obtiene `channels/alpha.json` desde este repositorio público. El sobre contiene un payload en Base64 y una firma RSA de 3072 bits con SHA-256. La clave pública está incluida en el ejecutable. Esta firma verifica las actualizaciones; no es una firma Authenticode del EXE.

El contenido firmado identifica una versión completa: secuencia, URL del asset de GitHub Releases, SHA-256 del ZIP, manifiesto y ejecutable, número de archivos y tamaño descomprimido. Se usa un canal explícito porque las versiones alpha se publican como prereleases y no dependen de `releases/latest`.

## Carpetas

```text
%LOCALAPPDATA%\Another Client\
  AnotherClientLauncher.exe       destino estable de Ac player.lnk
  ac.ico
  Versions\v<secuencia>-<hash>\   paquete completo y coherente
  Installer\
    current.json                 versión activa, con firma
    previous.json                versión anterior verificable
    Downloads\                   ZIP verificado
    Staging\                     extracción temporal
  Ac Player\                     perfiles, cachés y política de registro existentes
```

El cliente se instala por usuario. Crea `Ac player` en el escritorio y en el menú Inicio. Desde alpha 1.5.2, tanto esos accesos directos como el propio `Another Client.exe` consultan el canal antes de cargar el motor. La comprobación directa del EXE tiene un plazo de cuatro segundos: un canal inaccesible o inválido permite continuar con el cliente existente. Los modos explícitos de verificación, desarrollo y funcionamiento sin conexión conservan su comportamiento.

El EXE incorpora el puente de actualización y una copia del bootstrapper con la misma clave pública. El bootstrapper se extrae y verifica en `Installer/Bootstrapper/<hash>/`, consulta una sola vez el canal firmado y muestra **Updating Another Client...** únicamente cuando hay una versión superior. Usa la misma descarga, comprobación y extracción segura que el instalador. No modifica un EXE en uso: activa una versión completa en `Versions` y arranca esa versión. La respuesta temporal contiene únicamente el sobre público firmado, que el cliente vuelve a verificar antes de abrir el EXE nuevo. Un comprobante firmado, validado contra el hash y la ruta del nuevo EXE y eliminado del entorno al leerlo, evita repetir la consulta inmediatamente después de instalar; la siguiente apertura normal vuelve a consultar GitHub.

Se necesitan Windows 10/11 x64 y .NET Framework 4.8 o posterior. El instalador comprueba Visual C++ x64 y WebView2. Si falta Visual C++, descarga el redistribuible oficial y verifica su firma y su identidad antes de ejecutarlo; Windows puede pedir autorización UAC para ese componente compartido. Para WebView2 usa el bootstrapper firmado incluido en el paquete, con instalación por usuario. Los componentes presentes no se reinstalan. No se fuerza ningún reinicio. Un resultado que requiera reiniciar se comunica antes de abrir el cliente.

Mientras se ejecuta un instalador de Microsoft, Cancel queda temporalmente desactivado para evitar interrumpir la instalación de componentes compartidos. Las descargas y la extracción del cliente sí se pueden cancelar. La versión nueva no se activa hasta que las comprobaciones de componentes terminan correctamente. .NET Framework debe estar disponible para ejecutar el descargador administrado; no se incluye un instalador nativo de .NET.

La barra refleja los bytes descargados y extraídos. Los cambios de progreso se agrupan antes de pintar y se animan con WPF. La red, las huellas y la extracción trabajan fuera del hilo de la interfaz. Se reutilizan el material difuminado, el arrastre y la excepción para aplicaciones a pantalla completa del cliente.

El material es opcional: Windows 10 y los equipos con composición/transparencia desactivada, ahorro de batería, alto contraste o sesión remota usan fondo sólido. Las ventanas opacas se construyen sin activar Windows Runtime Composition. En equipos compatibles se conserva el desenfoque; un error al crearlo o liberarlo activa el respaldo sólido. Las pruebas `Material.Tests` simulan APIs ausentes y el error nativo E_INVALIDARG sin abrir ventanas. La prueba visual real en Windows 10 sigue siendo una comprobación manual separada.

Una actualización se prepara en `Staging`, se verifica por completo y se activa cambiando atómicamente `current.json`. Nunca se mezclan versiones. Cancelar no cambia la versión activa. Si falla el canal o la descarga, puede abrirse la versión instalada que siga verificándose. Una primera instalación necesita conexión. Una secuencia anterior no sustituye a otra más reciente ya instalada.

Los datos personales de `Ac Player`, incluidas las sesiones y la política antialt existentes, quedan fuera de las versiones y no se borran al actualizar. El instalador no añade comprobaciones antialt al servidor ni modifica las cuentas del VPS o la instalación del Roblox oficial.

## Publicar otra versión

**Subir un ZIP a una Release no activa una actualización.** Los clientes consultan `channels/alpha.json`, no el nombre del ZIP ni la Release marcada como más reciente. El canal debe firmarse y publicarse después de verificar el paquete. La secuencia tiene que aumentar tanto en el manifiesto externo como en `AnotherClient.ReleaseCatalog` incorporado en el EXE; renombrar el archivo o cambiar solo el manifiesto externo no cambia esa identidad. La herramienta de preparación ahora comprueba ambas copias y rechaza las discrepancias.

1. Preparar un ZIP completo y limpio con `client-manifest.json` y huellas actualizadas, excluyendo cuentas, sesiones y claves privadas.
2. Subirlo como asset de una Release. Conservar el nombre exacto devuelto por GitHub: puede convertir los espacios en puntos.
3. Compilar las herramientas con `installer/Build.ps1`. La clave pública está incluida. La clave privada del editor permanece protegida con DPAPI en su cuenta de Windows, fuera del repositorio y los paquetes. No sustituir la clave pública generando otra en una cuenta diferente.
4. Desde la cuenta del editor que conserva la clave, ejecutar Python 3.11 o posterior:

   ```powershell
   python installer/Prepare-Release.py "C:\ruta\cliente.zip" --tag alpha-1.6 --version "alpha 1.6" --sequence 7 --asset another.client.alpha.1.6.zip
   ```

5. Revisar `release-candidate.json` y `channel-candidate.json`. La herramienta verifica la firma del canal publicado y exige una secuencia superior. Esta debe coincidir con `updateChannelSequence` del manifiesto incorporado al cliente. `Publisher verify-client` comprueba además que los hashes del inventario incorporado coincidan con el manifiesto externo. Comprobar que tamaño y SHA-256 coinciden con el asset ya publicado en GitHub. Los módulos Avatar, Discord y Registration y su configuración pública están permitidos por el validador; las carpetas de cuentas, sesiones y claves siguen excluidas. Para preparación sin red se puede indicar `--previous-channel ruta/al/canal.json`; ese archivo también debe tener una firma válida y debe representar el último canal conocido.
6. Ejecutar `installer/Test.ps1` con el ZIP y el canal. Probar manualmente el recorrido visual en Windows cuando cambie el descargador.
7. Publicar solo el sobre firmado como `channels/alpha.json`. No publicar la clave DPAPI, sesiones, tokens ni el directorio `build`.

Conservar una copia de seguridad protegida de la cuenta y del material de firma. Si se pierde su clave privada, los instaladores existentes no aceptarán una identidad nueva: hay que planificar la distribución de un descargador que confíe en esa clave.

## Compilación y pruebas

`installer/Build.ps1` compila con .NET Framework y las proyecciones de Windows Runtime de Windows. `installer/Test.ps1` ejecuta pruebas sin abrir ventanas ni iniciar Player, en carpetas de prueba separadas de AppData y del escritorio. El directorio `build` contiene herramientas del editor que no se distribuyen junto al instalador.

Referencias: [assets de Releases](https://docs.github.com/en/rest/releases/assets#get-a-release-asset), [versión más reciente](https://docs.github.com/en/rest/releases/releases#get-the-latest-release), [carpetas de aplicaciones de Windows](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid), [WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [Visual C++](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files), [detección de .NET](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed).

## Corrección de integridad en alpha 1.5.6

La publicación comprueba también PlayerConfiguration.Load y PackageManifest.Load del EXE real, incluido el hash fijado del motor, sin iniciar el cliente. Así se rechazan paquetes cuyos hashes externos coinciden pero cuya política interna rechaza el lanzador incluido. No se elimina ni relaja la verificación.

El setup 1.0.4 muestra Updating Another Client... al actualizar una instalación existente; Downloading se conserva para primeras instalaciones. Tras un handoff firmado, el cliente renueva de forma atómica el lanzador estable únicamente si coincide con el hash aprobado de 1.0.3. Archivos desconocidos no se sobrescriben. El setup antiguo puede mostrar Downloading una última vez mientras instala esta corrección.

## Comprobación del arranque nativo en alpha 1.5.7

La 1.5.6 aún conservaba una segunda copia de la constante de integridad en NativeLaunch. La 1.5.7 actualiza esa referencia exacta sin desactivar el control. El publicador extrae también los dos ejecutables del motor e invoca exclusivamente el constructor de NativeLaunch con runtime nulo para comprobar sus hashes; nunca llama StartAsync ni arranca procesos. La regresión comprueba que la 1.5.6 falla, que la nueva versión supera ambas capas y que alterar el lanzador sigue causando rechazo.
