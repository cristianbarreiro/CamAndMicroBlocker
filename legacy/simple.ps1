# =====================================================================
#  BloqueoCamaraMicrofono.ps1
#  Herramienta simple con interfaz gráfica para bloquear/desbloquear
#  el acceso a la cámara y el micrófono en Windows 10.
#
#  Qué hace:
#    1) Aplica la política de sistema (HKLM) que fuerza a "Denegado"
#       el acceso de TODAS las apps a cámara y micrófono (el mismo
#       mecanismo que usa una GPO real). Es reversible con un clic.
#    2) Intenta además deshabilitar los dispositivos físicos de
#       cámara y micrófono en el Administrador de dispositivos, como
#       capa adicional. Esto depende de cómo el fabricante nombró
#       los dispositivos, así que puede no cubrir el 100% de los
#       casos en todos los equipos.
#
#  Requiere: Windows 10, PowerShell 5+, permisos de administrador
#  (el script se auto-eleva si no los tiene).
# =====================================================================

# --- Auto-elevación a administrador ---
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $scriptPath = $MyInvocation.MyCommand.Definition
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
    exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$regPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy"

function Set-PoliticaAcceso {
    param([int]$valor)  # 1 = Forzar Permitido, 2 = Forzar Denegado
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
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
        $_.Class -in @('Camera', 'Image') -or
        ($_.FriendlyName -match 'microphone|micr[oó]fono|webcam|c[aá]mara')
    }

    foreach ($d in $dispositivos) {
        try {
            if ($Deshabilitar) {
                Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop
            }
            else {
                Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction Stop
            }
        }
        catch {
            # Algunos dispositivos (ej. integrados en placa) pueden no
            # permitir deshabilitarse por software; se ignora ese caso.
        }
    }
    return $dispositivos.Count
}

# --- Interfaz gráfica ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Bloqueo de Cámara y Micrófono"
$form.Size = New-Object System.Drawing.Size(380, 230)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$lblEstado = New-Object System.Windows.Forms.Label
$lblEstado.Location = New-Object System.Drawing.Point(20, 20)
$lblEstado.Size = New-Object System.Drawing.Size(330, 40)
$lblEstado.Text = "Elegí una acción:"
$lblEstado.Font = New-Object System.Drawing.Font("Segoe UI", 10)
$form.Controls.Add($lblEstado)

$btnBloquear = New-Object System.Windows.Forms.Button
$btnBloquear.Text = "Bloquear cámara y micrófono"
$btnBloquear.Location = New-Object System.Drawing.Point(20, 70)
$btnBloquear.Size = New-Object System.Drawing.Size(320, 40)
$btnBloquear.BackColor = [System.Drawing.Color]::IndianRed
$btnBloquear.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnBloquear)

$btnDesbloquear = New-Object System.Windows.Forms.Button
$btnDesbloquear.Text = "Desbloquear cámara y micrófono"
$btnDesbloquear.Location = New-Object System.Drawing.Point(20, 120)
$btnDesbloquear.Size = New-Object System.Drawing.Size(320, 40)
$btnDesbloquear.BackColor = [System.Drawing.Color]::SeaGreen
$btnDesbloquear.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnDesbloquear)

$lblResultado = New-Object System.Windows.Forms.Label
$lblResultado.Location = New-Object System.Drawing.Point(20, 170)
$lblResultado.Size = New-Object System.Drawing.Size(330, 40)
$lblResultado.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($lblResultado)

$btnBloquear.Add_Click({
        Set-PoliticaAcceso -valor 2
        $n = Set-DispositivosFisicos -Deshabilitar
        $lblResultado.Text = "Bloqueado. Política aplicada + $n dispositivo(s) deshabilitado(s)."
    })

$btnDesbloquear.Add_Click({
        Quitar-PoliticaAcceso
        $n = Set-DispositivosFisicos
        $lblResultado.Text = "Desbloqueado. Política removida + $n dispositivo(s) habilitado(s)."
    })

[void]$form.ShowDialog()