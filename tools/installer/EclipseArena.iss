; Instalador de Eclipse Arena (MU MOBA · ONLINE).
; Se compila con tools\build-installer.ps1, que pasa las rutas y la IP por /D.
;   Instala el cliente, las DLL de Visual C++ que necesita, ZeroTier (se descarga al instalar, con
;   comprobacion de hash y firma), une el PC a la red del servidor y ofrece abrir el juego.

#ifndef ClientDir
  #error Falta /DClientDir
#endif
#ifndef ConfigFile
  #error Falta /DConfigFile
#endif
#ifndef StateFile
  #error Falta /DStateFile
#endif
#ifndef CrtDir
  #error Falta /DCrtDir
#endif
#ifndef OutDir
  #define OutDir "."
#endif
#ifndef ServerIP
  #define ServerIP "172.27.71.8"
#endif
#ifndef NetworkId
  #define NetworkId "3efa5cb78ac86a4b"
#endif
#ifndef AppVersion
  #define AppVersion "1.0"
#endif

; Lo que abren los accesos directos: el actualizador (muestra su ventana y lanza Main.exe al terminar).
#define Launcher "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """"{app}\Actualizar.ps1"""""

#define ZtUrl "https://download.zerotier.com/RELEASES/1.16.2/dist/ZeroTierOne.msi"
#define ZtSha256 "42514072b0fe44b8f66e0395bcd23a0b1d1642c28ed00831f1527b2f41b14670"

[Setup]
AppId={{6D3F8B1E-4C52-4B7A-9E0A-EC11B5E5A2A1}
AppName=Eclipse Arena
AppVersion={#AppVersion}
AppPublisher=Eclipse Arena (servidor privado de MU Online hecho por fans)
DefaultDirName={autopf}\Eclipse Arena
DefaultGroupName=Eclipse Arena
DisableProgramGroupPage=yes
OutputDir={#OutDir}
OutputBaseFilename=EclipseArena-Setup
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
Compression=lzma2/normal
SolidCompression=no
WizardStyle=modern
UninstallDisplayIcon={app}\Main.exe
DiskSpanning=no
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Dirs]
; el juego escribe config.ini y registros en su carpeta
Name: "{app}"; Permissions: users-modify

[Files]
Source: "{#ClientDir}\*"; DestDir: "{app}"; Excludes: "MuError.log,MuClient.lib,config.ini,config.ini.template"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ConfigFile}"; DestDir: "{app}"; DestName: "config.ini"; Flags: ignoreversion
; lo que queda instalado, para que el actualizador solo baje lo que cambie en las versiones siguientes
Source: "{#StateFile}"; DestDir: "{app}\actualizador"; DestName: "instalado.json"; Flags: ignoreversion
Source: "{#CrtDir}\vcruntime140.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#CrtDir}\vcruntime140_1.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#CrtDir}\msvcp140.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; el acceso directo abre el actualizador, que baja lo nuevo (si hay) y despues lanza Main.exe
Name: "{group}\Eclipse Arena"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "{#Launcher}"; IconFilename: "{app}\Main.exe"; WorkingDir: "{app}"
Name: "{group}\Desinstalar Eclipse Arena"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Eclipse Arena"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "{#Launcher}"; IconFilename: "{app}\Main.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "{#Launcher}"; WorkingDir: "{app}"; Description: "Abrir Eclipse Arena ahora"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
; los archivos que baja el actualizador no estan en el registro del instalador
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{commonpf32}\ZeroTier\One\zerotier-cli.bat"; Parameters: "leave {#NetworkId}"; Flags: runhidden; RunOnceId: "LeaveEclipseNetwork"

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NodeId: String;
  ZeroTierOk: Boolean;
  ZeroTierMessage: String;

function ZeroTierCli: String;
begin
  Result := ExpandConstant('{commonpf32}\ZeroTier\One\zerotier-cli.bat');
end;

function ZeroTierInstalled: Boolean;
begin
  Result := FileExists(ZeroTierCli);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  NodeId := '';
  ZeroTierOk := False;
end;

{ Descarga el MSI de ZeroTier (con comprobacion SHA-256) justo antes de copiar archivos. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (not ZeroTierInstalled) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#ZtUrl}', 'ZeroTierOne.msi', '{#ZtSha256}');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if DownloadPage.AbortedByUser then
          Log('Descarga de ZeroTier cancelada por el usuario.')
        else
          SuppressibleMsgBox('No se pudo descargar ZeroTier (' + GetExceptionMessage + ').' + #13#10 +
            'Comprueba tu conexion a internet e intentalo de nuevo.', mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

{ Ejecuta zerotier-cli y devuelve su salida. }
function RunZeroTier(const Params: String; var Output: String): Boolean;
var
  ResultCode: Integer;
  OutFile: String;
  Lines: TArrayOfString;
begin
  Output := '';
  OutFile := ExpandConstant('{tmp}\zt_out.txt');
  DeleteFile(OutFile);
  Result := Exec(ExpandConstant('{cmd}'), '/C ""' + ZeroTierCli + '" ' + Params + ' > "' + OutFile + '" 2>&1"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if LoadStringsFromFile(OutFile, Lines) and (GetArrayLength(Lines) > 0) then
    Output := Trim(Lines[0]);
end;

procedure SetUpZeroTier;
var
  ResultCode, I: Integer;
  Output: String;
  Parts: TStringList;
begin
  ZeroTierOk := False;

  if not ZeroTierInstalled then
  begin
    WizardForm.StatusLabel.Caption := 'Instalando ZeroTier (red privada del servidor)...';
    if not Exec('msiexec.exe', '/i "' + ExpandConstant('{tmp}\ZeroTierOne.msi') + '" /qn /norestart', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode) or ((ResultCode <> 0) and (ResultCode <> 3010)) then
    begin
      ZeroTierMessage := 'No se pudo instalar ZeroTier (codigo ' + IntToStr(ResultCode) + '). ' +
        'Instalalo a mano desde zerotier.com/download y unete a la red {#NetworkId}.';
      Exit;
    end;
  end;

  { el servicio tarda unos segundos en responder }
  WizardForm.StatusLabel.Caption := 'Conectando con la red del servidor...';
  for I := 1 to 30 do
  begin
    if RunZeroTier('info', Output) and (Pos('200 info', Output) = 1) then
      Break;
    Sleep(1000);
  end;

  if Pos('200 info', Output) <> 1 then
  begin
    ZeroTierMessage := 'ZeroTier no respondio. Reinicia el PC, abre ZeroTier y unete a la red {#NetworkId}.';
    Exit;
  end;

  Parts := TStringList.Create;
  try
    Parts.Delimiter := ' ';
    Parts.StrictDelimiter := True;
    Parts.DelimitedText := Output;
    if Parts.Count >= 3 then
      NodeId := Parts[2];
  finally
    Parts.Free;
  end;

  if not RunZeroTier('join {#NetworkId}', Output) then
  begin
    ZeroTierMessage := 'No se pudo unir a la red {#NetworkId}. Abre ZeroTier y unete a mano.';
    Exit;
  end;

  ZeroTierOk := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    SetUpZeroTier;
    if NodeId <> '' then
      { deja el ID en el portapapeles para pegarlo en un mensaje }
      Exec(ExpandConstant('{cmd}'), '/C <nul set /p="' + NodeId + '" | clip', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Text: String;
begin
  if CurPageID = wpFinished then
  begin
    if ZeroTierOk then
      Text := 'Eclipse Arena quedo instalado y tu PC ya esta conectado a la red del servidor.' + #13#10 + #13#10 +
        'ULTIMO PASO: el administrador debe aprobarte. Enviale este ID de ZeroTier (ya esta copiado en tu portapapeles, pegalo con Ctrl+V):' + #13#10 + #13#10 +
        '        ' + NodeId + #13#10 + #13#10 +
        'En cuanto te apruebe podras entrar al juego. Crea tu cuenta en http://{#ServerIP}:8080 (la web funciona una vez aprobado).'
    else
      Text := 'Eclipse Arena quedo instalado, pero falta un paso con la red:' + #13#10 + #13#10 + ZeroTierMessage;
    WizardForm.FinishedLabel.Caption := Text;
    WizardForm.FinishedLabel.AutoSize := True;
  end;
end;
