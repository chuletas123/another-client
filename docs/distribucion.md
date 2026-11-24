# Distribución privada

GitHub guarda las versiones del paquete y el VPS entrega a los jugadores la versión autorizada. El servidor descarga una versión concreta, comprueba su integridad y conserva una copia para atender las descargas del cliente.

Las credenciales de lectura del repositorio pertenecen al servidor. Los ejecutables y paquetes entregados a los jugadores no contienen tokens de GitHub.

## Preparación de una versión

1. Construir el paquete completo, con su ejecutable, recursos, catálogo de recuperación y manifiesto coherentes.
2. Comprobar sus huellas y confirmar que excluye perfiles, cuentas, sesiones y archivos locales de ejecución.
3. Validar las funciones modificadas. Para el paquete actual está pendiente la prueba manual de los atajos de captura y grabación.
4. Crear una Release de prueba y adjuntar el ZIP verificado. El ZIP automático de código fuente de GitHub no sustituye al paquete del cliente.
5. Verificar la copia descargada antes de activar el canal del instalador.

## Trabajo pendiente

Falta implementar la entrega desde el VPS, el manifiesto firmado del canal y el instalador/actualizador. El cliente comprobará actualizaciones antes de abrir los archivos de la versión que se va a sustituir. La instalación utilizará una carpeta temporal y activará una versión completa, con posibilidad de volver a la anterior si falla.

Este repositorio no modifica por sí mismo el cliente instalado ni configura el VPS.

Referencias: [crear repositorios](https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository), [descargar assets de Releases y sus permisos](https://docs.github.com/en/rest/releases/assets#get-a-release-asset).
