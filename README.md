# myLocalCloud — Cliente Multicloud con Files On-Demand (VFS)

**Versión:** 1.0  
**Desarrollada por:** android.ar  
**Desarrollador:** Alejandro Ferrer  

---

## 📌 Descripción

**myLocalCloud** es un cliente de sincronización multicloud (Google Drive, OneDrive, Dropbox, FTP y MEGA) para Windows. Funciona como un **Sistema de Archivos Virtual (VFS)** integrado nativamente en el Explorador de Windows mediante **Files On-Demand (Windows Cloud Files API - CfApi)**, permitiendo múltiples cuentas y garantizando que todos los datos sensibles y metadatos locales estén estrictamente protegidos mediante cifrado de grado militar.

La aplicación opera con un icono en la bandeja del sistema (System Tray, cerca del reloj, similar a Google Drive for Desktop) y realiza sincronización periódica en segundo plano.

---

## 🛡️ Arquitectura y Seguridad

1. **Protección de Credenciales y Tokens (Windows DPAPI)**:
   - Los tokens OAuth 2.0 (`AccessToken`, `RefreshToken`), credenciales FTP y claves maestras/sesiones de MEGA se cifran en memoria y disco mediante `ProtectedData.Protect` (`DataProtectionScope.CurrentUser` + vector de entropía de aplicación).
   - Los buffers en memoria se limpian inmediatamente mediante `CryptographicOperations.ZeroMemory`.

2. **Base de Datos Local Cifrada (SQLite + SQLCipher AES-256)**:
   - El archivo completo de la base de datos (`mylocalcloud_vfs.db`) se cifra a nivel de páginas con **AES-256-CBC**, `HMAC-SHA512` y `PBKDF2-HMAC-SHA512`.
   - La clave maestra de 256 bits se genera mediante CSPRNG y se persiste protegida exclusivamente con Windows DPAPI.

3. **Compatibilidad con `DateTimeOffset` en SQLite**:
   - Mapeo de `DateTimeOffset` a entero binario de 64 bits (`DateTimeOffsetToBinaryConverter`) para permitir ordenamiento (`ORDER BY`) y filtrado directo en SQL.
   - Migración automática de esquemas antiguos.

4. **Coexistencia Aislada con Google Drive for Desktop (`GoogleDriveFS.exe`)**:
   - Detección proactiva de instalaciones oficiales de Google Drive.
   - Aislamiento total de raíces de sincronización (`CfApi` Sync Roots) y carpetas locales para evitar conflictos con el cliente oficial de Google Drive.
   - Descriptores de archivo abiertos con `FileShare.ReadWrite | FileShare.Delete`.

5. **Virtualización Files On-Demand (`CfApi`)**:
   - Marcadores de posición (*placeholders*) de 0 bytes en disco.
   - Hidratación bajo demanda (descarga por chunks progresivos) al intentar abrir o acceder a los archivos desde el Explorador de Windows.

---

## 🏗️ Estructura del Proyecto (.NET 8)

```text
myLocalCloud/
├── myLocalCloud.sln
├── src/
│   ├── MyLocalCloud.Domain/            # Entidades, Enums, Modelos de Secretos y Contratos
│   ├── MyLocalCloud.Application/       # Casos de uso y clases base de proveedores
│   ├── MyLocalCloud.Infrastructure/    # DPAPI, SQLCipher, Conectores Multicloud y Sincronización
│   ├── MyLocalCloud.ShellIntegration/  # Integración nativa CfApi (cldapi.dll)
│   └── MyLocalCloud.UI/                # GUI WinUI 3 / Windows Forms, MVVM y System Tray
├── tests/
│   └── MyLocalCloud.UnitTests/         # 12 pruebas unitarias (Seguridad, SQLCipher, Providers, Sync, CfApi, Coexistencia)
├── installer/
│   └── MyLocalCloud.Installer/         # Asistente de instalación nativo para Windows
└── build-installer.ps1                 # Script de compilación y empaquetado del instalador
```

---

## 🚀 Compilación e Instalación

### Compilar y Ejecutar Pruebas
```powershell
dotnet test myLocalCloud.sln --configuration Release
```

### Generar Instalador Oficial
```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

El instalador se generará en `dist/myLocalCloud-Setup-v1.0.exe`.
