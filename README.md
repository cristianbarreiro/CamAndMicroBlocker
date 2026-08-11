# 🛡️ Cam & Microphone Blocker (CamMicBlocker)

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/Framework-.NET%2010.0%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Security-Dual--Layer%20Protection-4CAF50?style=for-the-badge&logo=windows-terminal&logoColor=white" alt="Dual Layer Security" />
  <img src="https://img.shields.io/badge/Language-Español%20%7C%20English-007ACC?style=for-the-badge" alt="i18n Support" />
</p>

**CamMicBlocker** es una aplicación de escritorio nativa para Windows (C# / .NET 10 / WPF) diseñada para **bloquear y desbloquear de forma rápida, confiable y 100% reversible el acceso a la cámara y al micrófono**.

Combina una protección de **doble capa** (Políticas de Grupo AppPrivacy + Deshabilitación de nodos PnP en el Administrador de Dispositivos) con un modelo de **elevación única (Single-UAC)** que evita carteles de confirmación repetitivos durante el uso.

---

## 📸 Screenshots & Vista Previa

> [!TIP]
> *Reemplazá los enlaces `assets/screenshots/...` con las imágenes de tu aplicación.*

<p align="center">
  <!-- SCREENSHOT PLACEHOLDER 1: Ventana Principal -->
  <img src="assets/screenshots/mainwindow_dark.png" alt="Ventana Principal CamMicBlocker" width="420" />
  <br />
  <sub><b>Figura 1:</b> Ventana Principal con Fluent Dark Theme, cabecera integrada y selector de idioma.</sub>
</p>

<br />

<div align="center">
  <table>
    <tr>
      <td align="center">
        <!-- SCREENSHOT PLACEHOLDER 2: Sección Como Funciona -->
        <img src="assets/screenshots/protection_overview.png" alt="Sección de Protección de Doble Capa" width="380" />
        <br />
        <sub><b>Figura 2:</b> Resumen de Protección de Doble Capa</sub>
      </td>
      <td align="center">
        <!-- SCREENSHOT PLACEHOLDER 3: Menú de la Bandeja (Tray Menu) -->
        <img src="assets/screenshots/tray_menu.png" alt="Menú de la Bandeja del Sistema" width="380" />
        <br />
        <sub><b>Figura 3:</b> Menú en la Bandeja del Sistema (Tray)</sub>
      </td>
    </tr>
  </table>
</div>

---

## ✨ Características Principales

- 🛡️ **Protección de Doble Capa (Dual-Layer Security)**:
  1. **Capa 1 (Políticas de Sistema)**: Aplica directivas de grupo `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy` para bloquear el acceso a aplicaciones de Windows y la Tienda.
  2. **Capa 2 (Controlador de Hardware PnP)**: Ejecuta llamadas nativas `CfgMgr32.dll` (`CM_Disable_DevNode` / `CM_Enable_DevNode`) para deshabilitar físicamente los nodos de dispositivo en el sistema.
- ⚡ **Cero Fatiga de UAC (Single-Elevation Model)**:
  - Solicita permisos de administrador **una única vez al iniciar** (`requireAdministrator`).
  - Todas las alternancias subsiguientes (atajo de teclado, menú de la bandeja o switches) ejecutan **al instante (0 ms de latencia IPC) sin volver a pedir UAC**.
- 🎨 **Diseño Moderno Fluent Dark UI**:
  - Cabecera integrada (*Custom Title Bar*) de 38px con esquinas redondeadas (12px) y sombra paralela.
  - Barra de desplazamiento personalizada ultradelgada de 8px (*Custom ScrollBar*).
- 🌐 **Soporte Multilingüe Dinámico (ES / EN)**:
  - Selector segmentado en píldora `[ ES | EN ]` en el pie de la ventana.
  - Cambio de idioma instantáneo en tiempo real sin reiniciar la aplicación.
- ⌨️ **Atajo Global de Teclado**: Alterná el bloqueo instantáneo con **`Ctrl + Alt + B`**.
- 📌 **Integración con la Bandeja del Sistema (System Tray)**:
  - Se minimiza silenciosamente a la bandeja al cerrar `(X)`.
  - Iconos dinámicos en memoria (Candado Verde = Permitido, Candado Rojo = Bloqueado).

---

## 🛠️ Requisitos del Sistema

- **Sistema Operativo**: Windows 10 o Windows 11 (64-bit).
- **Runtime**: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (o ejecutase la versión *Self-Contained* portable).
- **Privilegios**: Requiere ejecución como Administrador (solicitado automáticamente por UAC al iniciar).

---

## 🚀 Instalación y Uso

### Opción 1: Ejecutable Portable (Recomendado)
1. Descargá el ejecutable portable `CamMicBlocker.exe` desde la sección de Releases.
2. Hacé doble clic sobre `CamMicBlocker.exe`.
3. Aceptá la solicitud de UAC (Administrador) **una sola vez**.
4. ¡Listo! La ventana principal se abrirá y el icono aparecerá en la barra de tareas junto al reloj.

---

## 💻 Compilación desde el Código Fuente

Si deseás compilar el proyecto manualmente con el SDK de .NET 10:

```powershell
# 1. Clonar el repositorio
git clone https://github.com/tu-usuario/Cam-MicroBlocker.git
cd Cam-MicroBlocker

# 2. Restaurar y compilar la solución
dotnet build CamMicBlocker.sln

# 3. Ejecutar las pruebas unitarias (26 tests)
dotnet test CamMicBlocker.sln

# 4. Publicar el ejecutable portable de un solo archivo (Single-File)
dotnet publish src/CamMicBlocker/CamMicBlocker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish_out
```

El ejecutable listo para distribuir se generará en la carpeta `publish_out\CamMicBlocker.exe`.

---

## 📐 Arquitectura de Código

El proyecto sigue una arquitectura limpia en capas basada en **DDD (Domain-Driven Design)**:

```text
Cam&MicroBlocker/
├── src/
│   └── CamMicBlocker/
│       ├── App.xaml / App.xaml.cs    # Inicio de aplicación y logging de 8 pasos
│       ├── Application/              # Servicios de Orquestación (BlockingService, LanguageService)
│       ├── Domain/                   # Modelos puros e Interfaces (IDeviceDetector, IDeviceController)
│       ├── Infrastructure/           # P/Invoke CfgMgr32, Registro HKLM, WMI DeviceDetector
│       ├── Logging/                  # Serilog + CrashReporter (Dumps JSON post-mortem)
│       └── UI/                       # Vistas WPF (MainWindow, NotificationWindow, TrayIcon, Localization)
├── tests/
│   └── CamMicBlocker.Tests/          # Suite de Pruebas Unitarias xUnit
└── AGENTS.md                         # Guía de arquitectura y reglas de seguridad para agentes AI
```

---

## 📄 Licencia

Este proyecto está bajo la Licencia **MIT**. Consulta el archivo `LICENSE` para más detalles.
