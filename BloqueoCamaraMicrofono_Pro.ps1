# =====================================================================
#  BloqueoCamaraMicrofono_Pro.ps1
#
#  Versión "pro": vive en la bandeja del sistema, sin ventanas
#  molestas. Incluye:
#    - Ícono en bandeja que cambia de color (verde = libre, rojo = bloqueado)
#    - Atajo de teclado global: Ctrl+Alt+B (togglea bloqueo/desbloqueo)
#    - Notificación en pantalla al togglear (como el aviso de volumen)
#    - Opción "Iniciar con Windows" desde el menú
#
#  Requiere: Windows 10, PowerShell 5+, permisos de administrador
#  (se auto-eleva solo).
# =====================================================================

# --- Auto-elevación a administrador ---
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $scriptPath = $MyInvocation.MyCommand.Definition
    Start-Process powershell.exe -ArgumentList "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
    exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --- Registro de diagnóstico ---
$logPath = Join-Path $env:TEMP "CamMicBlocker.log"
"[$(Get-Date -Format 'HH:mm:ss')] === Inicio del script (usuario: $env:USERNAME, admin: OK) ===" | Out-File -FilePath $logPath -Append -Encoding UTF8

try {

"[$(Get-Date -Format 'HH:mm:ss')] Compilando HotKeyForm..." | Out-File -FilePath $logPath -Append -Encoding UTF8

# --- Ventana oculta para capturar el atajo de teclado global ---
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class HotKeyForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event EventHandler HotKeyPressed;

    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_B = 0x42; // tecla B

    public HotKeyForm()
    {
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        this.Opacity = 0;
        this.Load += (s, e) => { this.Hide(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_B);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(this.Handle, HOTKEY_ID);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            if (HotKeyPressed != null) HotKeyPressed(this, EventArgs.Empty);
        }
        base.WndProc(ref m);
    }
}
"@ -ReferencedAssemblies System.Windows.Forms, System.Drawing

"[$(Get-Date -Format 'HH:mm:ss')] HotKeyForm compilado OK." | Out-File -FilePath $logPath -Append -Encoding UTF8

# =====================================================================
#  Lógica de bloqueo (registro + dispositivos físicos)
# =====================================================================
$regPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy"
$runKey  = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runName = "BloqueoCamaraMicrofono"

function Set-PoliticaAcceso {
    param([int]$valor)  # 1 = Forzar Permitido, 2 = Forzar Denegado
    if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    New-ItemProperty -Path $regPath -Name "LetAppsAccessCamera" -PropertyType DWord -Value $valor -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "LetAppsAccessMicrophone" -PropertyType DWord -Value $valor -Force | Out-Null
}

function Quitar-PoliticaAcceso {
    Remove-ItemProperty -Path $regPath -Name "LetAppsAccessCamera" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $regPath -Name "LetAppsAccessMicrophone" -ErrorAction SilentlyContinue
}

function Set-DispositivosFisicos {
    param([switch]$Deshabilitar)
    $dispositivos = Get-PnpDevice -PresentOnly | Where-Object {
        $_.Class -in @('Camera','Image') -or
        ($_.FriendlyName -match 'microphone|micr[oó]fono|webcam|c[aá]mara')
    }
    foreach ($d in $dispositivos) {
        try {
            if ($Deshabilitar) { Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop }
            else               { Enable-PnpDevice  -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop }
        } catch { }
    }
}

function Test-EstadoBloqueado {
    $v = Get-ItemProperty -Path $regPath -Name "LetAppsAccessCamera" -ErrorAction SilentlyContinue
    return ($v -and $v.LetAppsAccessCamera -eq 2)
}

# =====================================================================
#  Íconos generados en memoria (verde = libre, rojo = bloqueado)
# =====================================================================
function New-IconoCandado {
    param([System.Drawing.Color]$Color)
    $bmp = New-Object System.Drawing.Bitmap 32,32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush($Color)
    $pen = New-Object System.Drawing.Pen($Color, 3)
    # cuerpo del candado
    $g.FillRectangle($brush, 6, 14, 20, 15)
    # arco
    $g.DrawArc($pen, 9, 4, 14, 16, 180, 180)
    $g.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    return $icon
}
$iconoLibre     = New-IconoCandado -Color ([System.Drawing.Color]::SeaGreen)
$iconoBloqueado = New-IconoCandado -Color ([System.Drawing.Color]::IndianRed)

"[$(Get-Date -Format 'HH:mm:ss')] Iconos generados OK." | Out-File -FilePath $logPath -Append -Encoding UTF8

# =====================================================================
#  Notificación tipo overlay (como el aviso de volumen de Windows)
# =====================================================================
function Mostrar-Notificacion {
    param([string]$Texto, [System.Drawing.Color]$Color)

    $notif = New-Object System.Windows.Forms.Form
    $notif.FormBorderStyle = 'None'
    $notif.StartPosition = 'Manual'
    $notif.ShowInTaskbar = $false
    $notif.TopMost = $true
    $notif.Size = New-Object System.Drawing.Size(280, 70)
    $notif.BackColor = [System.Drawing.Color]::FromArgb(30,30,30)
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $notif.Location = New-Object System.Drawing.Point(($screen.Width - 300), ($screen.Height - 100))
    $notif.Opacity = 0

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $Texto
    $lbl.ForeColor = $Color
    $lbl.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
    $lbl.TextAlign = 'MiddleCenter'
    $lbl.Dock = 'Fill'
    $notif.Controls.Add($lbl)

    $notif.Show()

    $fadeIn = New-Object System.Windows.Forms.Timer
    $fadeIn.Interval = 15
    $fadeIn.Add_Tick({
        if ($notif.Opacity -lt 0.95) { $notif.Opacity += 0.08 }
        else { $fadeIn.Stop() }
    })
    $fadeIn.Start()

    $espera = New-Object System.Windows.Forms.Timer
    $espera.Interval = 1400
    $espera.Add_Tick({
        $espera.Stop()
        $fadeOut = New-Object System.Windows.Forms.Timer
        $fadeOut.Interval = 15
        $fadeOut.Add_Tick({
            if ($notif.Opacity -gt 0.08) { $notif.Opacity -= 0.08 }
            else { $fadeOut.Stop(); $notif.Close() }
        })
        $fadeOut.Start()
    })
    $espera.Start()
}

# =====================================================================
#  Toggle principal
# =====================================================================
$script:bloqueado = Test-EstadoBloqueado

function Actualizar-UI {
    if ($script:bloqueado) {
        $notifyIcon.Icon = $iconoBloqueado
        $notifyIcon.Text = "Cámara y micrófono: BLOQUEADOS"
        $itemBloquear.Enabled = $false
        $itemDesbloquear.Enabled = $true
    } else {
        $notifyIcon.Icon = $iconoLibre
        $notifyIcon.Text = "Cámara y micrófono: libres"
        $itemBloquear.Enabled = $true
        $itemDesbloquear.Enabled = $false
    }
}

function Toggle-Bloqueo {
    if ($script:bloqueado) {
        Quitar-PoliticaAcceso
        Set-DispositivosFisicos
        $script:bloqueado = $false
        Mostrar-Notificacion -Texto "Cámara y micrófono DESBLOQUEADOS" -Color ([System.Drawing.Color]::LightGreen)
    } else {
        Set-PoliticaAcceso -valor 2
        Set-DispositivosFisicos -Deshabilitar
        $script:bloqueado = $true
        Mostrar-Notificacion -Texto "Cámara y micrófono BLOQUEADOS" -Color ([System.Drawing.Color]::IndianRed)
    }
    Actualizar-UI
}

# =====================================================================
#  Ícono de bandeja + menú
# =====================================================================
"[$(Get-Date -Format 'HH:mm:ss')] Creando NotifyIcon..." | Out-File -FilePath $logPath -Append -Encoding UTF8

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = $iconoLibre
$notifyIcon.Visible = $true

"[$(Get-Date -Format 'HH:mm:ss')] NotifyIcon.Visible = $($notifyIcon.Visible)" | Out-File -FilePath $logPath -Append -Encoding UTF8

$menu = New-Object System.Windows.Forms.ContextMenuStrip

$itemBloquear = $menu.Items.Add("Bloquear ahora")
$itemBloquear.Add_Click({ if (-not $script:bloqueado) { Toggle-Bloqueo } })

$itemDesbloquear = $menu.Items.Add("Desbloquear ahora")
$itemDesbloquear.Add_Click({ if ($script:bloqueado) { Toggle-Bloqueo } })

[void]$menu.Items.Add("-")

$itemInicio = New-Object System.Windows.Forms.ToolStripMenuItem("Iniciar con Windows")
$itemInicio.CheckOnClick = $true
$itemInicio.Checked = [bool](Get-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue)
$itemInicio.Add_Click({
    if ($itemInicio.Checked) {
        $exe = "powershell.exe"
        $arg = "-WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        New-ItemProperty -Path $runKey -Name $runName -PropertyType String -Value "$exe $arg" -Force | Out-Null
    } else {
        Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
    }
})
[void]$menu.Items.Add($itemInicio)

[void]$menu.Items.Add("-")

$itemAtajo = $menu.Items.Add("Atajo: Ctrl + Alt + B")
$itemAtajo.Enabled = $false

$itemSalir = $menu.Items.Add("Salir")
$itemSalir.Add_Click({
    $notifyIcon.Visible = $false
    [System.Windows.Forms.Application]::Exit()
})

$notifyIcon.ContextMenuStrip = $menu
$notifyIcon.add_MouseClick({
    param($sender, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) { Toggle-Bloqueo }
})

Actualizar-UI

# --- Registrar atajo global Ctrl+Alt+B ---
$hotkeyForm = New-Object HotKeyForm
$hotkeyForm.add_HotKeyPressed({ Toggle-Bloqueo })
$hotkeyForm.Show()
$hotkeyForm.Hide()

"[$(Get-Date -Format 'HH:mm:ss')] Todo listo. Entrando al loop principal (Application.Run)." | Out-File -FilePath $logPath -Append -Encoding UTF8

# --- Loop principal (mantiene vivo el ícono de bandeja) ---
[System.Windows.Forms.Application]::Run()

}
catch {
    $msg = "[$(Get-Date -Format 'HH:mm:ss')] ERROR: $($_.Exception.Message)`r`n$($_.InvocationInfo.PositionMessage)"
    $msg | Out-File -FilePath $logPath -Append -Encoding UTF8
    [System.Windows.Forms.MessageBox]::Show($msg, "Error en BloqueoCamaraMicrofono", "OK", "Error") | Out-Null
}