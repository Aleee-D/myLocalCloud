# 🌳☁ myLocalCloud v1.1 — Cliente Multicloud con Unidades de Disco Virtuales (Files On-Demand)

**Versión:** 1.1  
**Desarrollada por:** android.ar  
**Desarrollador:** Alejandro Ferrer  

---

## 📌 Descripción y Novedades de la Versión 1.1

**myLocalCloud v1.1** es un cliente de sincronización multicloud (**Google Drive, OneDrive, Dropbox, FTP y MEGA**) para Windows que funciona como un **Sistema de Archivos Virtual (VFS)** integrado nativamente en el Explorador de Windows.

### ✨ Novedades principales en v1.1:
1. **Unidades de Disco Virtuales por Usuario (`M:`, `N:`, `O:`, ..., `Z:`)**:
   - Al registrar cada usuario de nube se selecciona una **letra de unidad de disco virtual** dedicada donde aparecen los archivos de esa cuenta dentro del Explorador de Windows.
2. **Archivos en Modo Referencia On-Demand (Descarga al Abrir) o Copia Local**:
   - **Referencia en Nube (0 bytes locales)**: Los archivos aparecen como referencias en la unidad virtual sin ocupar espacio local y **se descargan de la nube en el mismo momento en que el usuario los abre**.
   - **Copia Local Completa**: Los archivos se descargan y mantienen localmente en la unidad virtual.
3. **Multi-Usuario (Misma Nube o Diferentes Nubes)**:
   - Permite registrar múltiples usuarios de **la misma nube** (ej. 3 cuentas distintas de Google Drive, 2 de MEGA, etc.) o de **diferentes nubes**, asignando a cada cuenta su propia letra de unidad virtual para sumar y aprovechar al máximo el espacio disponible de cada usuario.
4. **Nuevo Diseño Visual (Paleta Celeste y Gris) y Nuevo Logotipo (Árbol con Copa de Nube)**:
   - Interfaz rediseñada con **Celeste (`#0EA5E9` / `#38BDF8` / `#E0F2FE`)** y **Gris (`#475569` / `#E2E8F0`)** como colores primarios.
   - Nuevo logotipo oficial: **un árbol cuya copa es una nube**, presente en el icono cerca del reloj de Windows, barra de título, panel de control e instalador.

---

## 🛡️ Arquitectura y Seguridad

1. **Protección de Credenciales y Tokens (Windows DPAPI)**:
   - Los tokens OAuth 2.0 (`AccessToken`, `RefreshToken`), credenciales FTP y claves maestras/sesiones de MEGA se cifran mediante `ProtectedData.Protect` (`DataProtectionScope.CurrentUser` + vector de entropía de aplicación) y se limpian de la RAM con `CryptographicOperations.ZeroMemory`.
2. **Base de Datos Local Cifrada (SQLite + SQLCipher AES-256)**:
   - El archivo completo `mylocalcloud_vfs.db` se cifra a nivel de páginas con **AES-256-CBC**, `HMAC-SHA512` y `PBKDF2-HMAC-SHA512`.
3. **Coexistencia Aislada con Google Drive for Desktop (`GoogleDriveFS.exe`)**:
   - Aislamiento completo de raíces `CfApi`, letras de unidad virtual y descriptores de archivo (`FileShare.ReadWrite | FileShare.Delete`).

---

## 🚀 Compilación e Instalación

### Ejecutar Suite de Pruebas Unitarias (15 pruebas)
```powershell
dotnet test myLocalCloud.sln --configuration Release
```

### Generar Instalador Oficial v1.1
```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Archivos generados en `dist/`:
- `dist/myLocalCloud-Setup-v1.1.exe` (Instalador oficial para Windows con asistente gráfico)
- `dist/myLocalCloud-Portable-v1.1.exe` (Ejecutable autónomo de un solo archivo)
