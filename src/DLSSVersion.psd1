@{
    RootModule = 'DLSSVersion.psm1'
    ModuleVersion = '0.1.0'
    GUID = 'e4b9d4fb-fa91-4242-922d-d5f6adac415b'
    Author = 'DLSS Version Toolkit Contributors'
    Description = 'Check and upgrade NVIDIA DLSS override versions on Windows'
    PowerShellVersion = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport = @('Get-DLSSVersions', 'Get-DLSSLatestVersion', 'Start-DLSSUpgrade', 'Get-StreamlineVersions', 'Sync-DLSSVersions', 'Compare-DLSSAllSources')
    VariablesToExport = @()
    CmdletsToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('DLSS', 'NVIDIA', 'NGX', 'Version', 'Upgrade')
            ProjectUri = 'https://github.com/scubamount/dlss-version-toolkit'
            LicenseUri = 'https://github.com/scubamount/dlss-version-toolkit/blob/main/LICENSE'
            ReleaseNotes = 'Initial release'
        }
    }
}
