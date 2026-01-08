using System;
using System.IO;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Collections.Generic;

namespace EasyShare
{
    public static class ShareManager
    {
        public const string TempFolderName = "PartageTemp";
        public static readonly string TempFolderPath = Path.Combine("C:\\", TempFolderName);

        // Using temporary script files to avoid quoting and CLIXML issues
        public static string ExecutePowerShell(string script)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"easyshare_{Guid.NewGuid():N}.ps1");
            try
            {
                // Set ErrorAction preference at the top of the temp file
                string fullScript = "$ErrorActionPreference = 'Stop'\n" + script;
                // Use Unicode (UTF-16) as it's the most reliable for PowerShell scripts on Windows with accents
                File.WriteAllText(tempFile, fullScript, System.Text.Encoding.Unicode);

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null) throw new Exception("Failed to start PowerShell process.");

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            // Strip common PowerShell error prefixes if they exist
                            string cleanError = error.Trim();
                            if (cleanError.Contains(" : ")) 
                                cleanError = cleanError.Substring(cleanError.IndexOf(" : ") + 3);
                            
                            // If it's still CLIXML (unlikely with -File), at least show the first line of output
                            if (cleanError.StartsWith("#< CLIXML"))
                                throw new Exception("Erreur système lors du partage. Vérifiez que le dossier n'est pas utilisé.");

                            throw new Exception(cleanError);
                        }
                        throw new Exception($"PowerShell error {process.ExitCode}");
                    }

                    return output;
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        public static void CreateUser(string userName, string password)
        {
            // 1. Create user and add to Administrators
            string script = $@"
                $pass = ConvertTo-SecureString '{password}' -AsPlainText -Force
                if (-not (Get-LocalUser -Name ""{userName}"" -ErrorAction SilentlyContinue)) {{
                    New-LocalUser -Name ""{userName}"" -Password $pass -FullName ""{userName}"" -Description ""Shared Account""
                    $adminGroup = Get-LocalGroup -Sid ""S-1-5-32-544""
                    Add-LocalGroupMember -Group $adminGroup.Name -Member ""{userName}""
                }}
                # 2. Enable Firewall rules (non-terminating)
                $ErrorActionPreference = 'Continue'
                Enable-NetFirewallRule -DisplayGroup ""*File and Printer Sharing*"" -ErrorAction SilentlyContinue
                Enable-NetFirewallRule -Name NetPres-In-TCP-NoScope,NetPres-Out-TCP-NoScope,SMBDirect-In-NetBios,SMBDirect-In-TCP -ErrorAction SilentlyContinue
            ";
            ExecutePowerShell(script);
        }

        public static void SetPermissions(string folderPath, string userName)
        {
            try
            {
                DirectoryInfo dInfo = new DirectoryInfo(folderPath);
                DirectorySecurity dSecurity = dInfo.GetAccessControl();

                // Full Control for the shared user
                FileSystemAccessRule ruleUser = new FileSystemAccessRule(userName, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
                dSecurity.AddAccessRule(ruleUser);

                // Full Control for Everyone
                SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                FileSystemAccessRule ruleEveryone = new FileSystemAccessRule(everyoneSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
                dSecurity.AddAccessRule(ruleEveryone);

                dInfo.SetAccessControl(dSecurity);
            }
            catch (Exception ex)
            {
                // If it's not an NTFS drive, setting ACLs might fail. We log but don't crash.
                Debug.WriteLine($"ACL Error: {ex.Message}");
                // We don't throw here to allow SMB sharing to proceed anyway
            }
        }

        public static void ShareFolder(string folderPath, string shareName, string userName, bool isHidden)
        {
            string finalShareName = isHidden && !shareName.EndsWith("$") ? shareName + "$" : shareName;
            
            string script = $@"
                if (Get-SmbShare -Name ""{finalShareName}"" -ErrorAction SilentlyContinue) {{
                    Remove-SmbShare -Name ""{finalShareName}"" -Force
                }}
                $everyone = (New-Object System.Security.Principal.SecurityIdentifier(""S-1-1-0"")).Translate([System.Security.Principal.NTAccount]).Value
                New-SmbShare -Name ""{finalShareName}"" -Path ""{folderPath}"" -FullAccess ""{userName}"", $everyone
            ";
            ExecutePowerShell(script);
        }

        public static List<string> GetEasyShareShares()
        {
            // We want to list shares but EXCLUDE system admin shares like C$, D$, ADMIN$, IPC$, and print$
            // We use a regex to catch any single letter followed by $ (e.g. D$, F$)
            string script = "Get-SmbShare | Where-Object { $_.Path -ne $null -and ($_.Name -notlike '*$' -or ($_.Name -notmatch '^[A-Z]\\$$' -and $_.Name -ne 'ADMIN$' -and $_.Name -ne 'IPC$' -and $_.Name -ne 'print$')) } | Select-Object -ExpandProperty Name";
            try
            {
                string output = ExecutePowerShell(script);
                var list = new List<string>(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                return list;
            }
            catch { return new List<string>(); }
        }

        public static List<string> GetEasyShareUsers()
        {
            // We look for users created by us (they have 'Shared Account' description)
            string script = "Get-LocalUser | Where-Object { $_.Description -eq 'Shared Account' } | Select-Object -ExpandProperty Name";
            try
            {
                string output = ExecutePowerShell(script);
                var list = new List<string>(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                return list;
            }
            catch { return new List<string>(); }
        }

        public static void RemoveShare(string shareName)
        {
            string script = $"Remove-SmbShare -Name \"{shareName}\" -Force";
            ExecutePowerShell(script);
        }

        public static void RemoveUser(string userName)
        {
            string script = $"Remove-LocalUser -Name \"{userName}\"";
            ExecutePowerShell(script);
        }

        public static void Cleanup(string userName)
        {
            // Full cleanup as a fallback
            foreach (var share in GetEasyShareShares()) try { RemoveShare(share); } catch { }
            foreach (var user in GetEasyShareUsers()) try { RemoveUser(user); } catch { }

            // Delete PartageTemp if it exists
            if (Directory.Exists(TempFolderPath))
            {
                try { Directory.Delete(TempFolderPath, true); } catch { }
            }
        }
    }
}
