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

Trabaja por usuario, sin UAC. Crea `Ac player` en el escritorio y en el menú Inicio. Esos accesos directos consultan el canal antes de abrir la versión seleccionada. Abrir directamente un EXE dentro de `Versions` omite esa comprobación.

La barra refleja los bytes descargados y extraídos. Los cambios de progreso se agrupan antes de pintar y se animan con WPF. La red, las huellas y la extracción trabajan fuera del hilo de la interfaz. Se reutilizan el material difuminado, el arrastre y la excepción para aplicaciones a pantalla completa del cliente.

Una actualización se prepara en `Staging`, se verifica por completo y se activa cambiando atómicamente `current.json`. Nunca se mezclan versiones. Cancelar no cambia la versión activa. Si falla el canal o la descarga, puede abrirse la versión instalada que siga verificándose. Una primera instalación necesita conexión. Una secuencia anterior no sustituye a otra más reciente ya instalada.

Los datos personales de `Ac Player`, incluidas las sesiones y la política antialt existentes, quedan fuera de las versiones y no se borran al actualizar. El instalador no añade comprobaciones antialt al servidor ni modifica las cuentas del VPS o la instalación del Roblox oficial.

## Publicar otra versión

1. Preparar un ZIP completo y limpio con `client-manifest.json` y huellas actualizadas, excluyendo cuentas, sesiones y claves privadas.
2. Subirlo como asset de una Release. Conservar el nombre exacto devuelto por GitHub: puede convertir los espacios en puntos.
3. Compilar las herramientas con `installer/Build.ps1`. La clave pública está incluida. La clave privada del editor permanece protegida con DPAPI en su cuenta de Windows, fuera del repositorio y los paquetes. No sustituir la clave pública generando otra en una cuenta diferente.
4. Desde la cuenta del editor que conserva la clave, ejecutar Python 3.11 o posterior:

   ```powershell
   python installer/Prepare-Release.py "C:\ruta\cliente.zip" --tag alpha-1.6 --version "alpha 1.6" --sequence 2 --asset another.client.alpha.1.6.zip
   ```

5. Revisar `release-candidate.json` y `channel-candidate.json`. La secuencia debe aumentar respecto del canal publicado. Comprobar que tamaño y SHA-256 coinciden con el asset ya publicado en GitHub.
6. Ejecutar `installer/Test.ps1` con el ZIP y el canal. Probar manualmente el recorrido visual en Windows cuando cambie el descargador.
7. Publicar solo el sobre firmado como `channels/alpha.json`. No publicar la clave DPAPI, sesiones, tokens ni el directorio `build`.

Conservar una copia de seguridad protegida de la cuenta y del material de firma. Si se pierde su clave privada, los instaladores existentes no aceptarán una identidad nueva: hay que planificar la distribución de un descargador que confíe en esa clave.

## Compilación y pruebas

`installer/Build.ps1` compila con .NET Framework y las proyecciones de Windows Runtime de Windows. `installer/Test.ps1` ejecuta pruebas sin abrir ventanas ni iniciar Player, en carpetas de prueba separadas de AppData y del escritorio. El directorio `build` contiene herramientas del editor que no se distribuyen junto al instalador.

Referencias: [assets de Releases](https://docs.github.com/en/rest/releases/assets#get-a-release-asset), [versión más reciente](https://docs.github.com/en/rest/releases/releases#get-the-latest-release), [carpetas de aplicaciones de Windows](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid).
