using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Plugins
{
    public class Mindustry
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Mindustry",
            author = "Original: Andy |Modified: Majorchuckles",
            description = "WindowsGSM plugin for Mindustry",
            version = "1.2.2",
            url = "https://github.com/majorchuckles/WindowsGSM.Mindustry-2025",
            color = "#800080"
        };


        // - Standard Constructor and properties
        public Mindustry(ServerConfig serverData) => _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;


        // - Game server Fixed variables
        public string StartPath = "server-release.jar";
        public string FullName = "Mindustry";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = new A2S(); // just a guess


        // - Game server default values
        public string Port = "6567";
        public string QueryPort = "6859";
        public string Defaultmap = "None"; 
        public string Maxplayers = "10"; 
        public string Additional = "host";


        // - Create a default cfg for the game server after installation
        public async void CreateServerCFG()
        {

        }


        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            
            // Set custom java path
            var javahomepath = Environment.GetEnvironmentVariable("JAVA_HOME");
            // Uncomment the one below to hard code a path if Environment Variables are not working
            //var javapath = "C:\\Program Files\\Java\\jdk-17\\bin\\java.exe";
            if (javahomepath.Length == 0)
            {
                Error = "Java is not installed";
                return null;
            }
            
            // Prepare start parameter
            var param = new StringBuilder($"-jar {StartPath} ");
            param.Append(string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $"config port {_serverData.ServerPort},");
            param.Append(string.IsNullOrWhiteSpace(_serverData.ServerQueryPort) ? string.Empty : $"config socketInput true,");
            param.Append(string.IsNullOrWhiteSpace(_serverData.ServerQueryPort) ? string.Empty : $"config socketInputPort {_serverData.ServerQueryPort},");
            param.Append(string.IsNullOrWhiteSpace(_serverData.ServerParam) ? string.Empty : $"{_serverData.ServerParam}");

            // Prepare Process
            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = javapath,
                    Arguments = param.ToString(),
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            // Set up Redirect Input and Output to WindowsGSM Console if EmbedConsole is on
            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;

                // Start Process
                try
                {
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    return p;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    return null; // return null if fail to start
                }
            }

            // Start Process
            try
            {
                p.Start();
                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null; // return null if fail to start
            }
        }


        // - Stop server function
        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    // Send "stop" command to StandardInput stream if EmbedConsole is on
                    p.StandardInput.WriteLine("stop");
                    p.StandardInput.WriteLine("exit");
                }
                else
                {
                    // Send "stop" command to game server process MainWindow
                    Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "stop");
                    Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "exit");
                }
            });
        }


        // - Install server function
        public async Task<Process> Install()
        {
            // Try getting the latest version and build
            var build = await GetRemoteBuild(); // "v125.1"
            if (string.IsNullOrWhiteSpace(build)) { return null; }

            // Download the latest server-release.jar to /serverfiles
            try
            {
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync($"https://github.com/Anuken/Mindustry/releases/download/{build}/server-release.jar", ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }

            // Create file that houses the version last downloaded.
            var versionFile = ServerPath.GetServersServerFiles(_serverData.ServerID, "mindustry_version.txt");
            File.WriteAllText(versionFile, $"{build}");

            return null;
        }



        // - Update server function
        public async Task<Process> Update()
        {
            // Delete the old server.jar
            var mindustryServerJar = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (File.Exists(mindustryServerJar))
            {
                if (await Task.Run(() =>
                {
                    try
                    {
                        File.Delete(mindustryServerJar);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Error = e.Message;
                        return false;
                    }
                }));
            } 

            // Try getting the latest version and build
            var build = await GetRemoteBuild(); // "v125.1"
            if (string.IsNullOrWhiteSpace(build)) { return null; }

            // Download the latest paper.jar to /serverfiles
            try
            {
                using (var webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync($"https://github.com/Anuken/Mindustry/releases/download/{build}/server-release.jar", ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));

                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }


            // Create file that houses the version last downloaded.
            var versionFile = ServerPath.GetServersServerFiles(_serverData.ServerID, "mindustry_version.txt");
            File.WriteAllText(versionFile, $"{build}");

            return null;
        }


        // - Check if the installation is successful
        public bool IsInstallValid()
        {
            var mindustryServerJar = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (File.Exists(mindustryServerJar))
                {
                    return true;
                }
            else
                {
                    return false;
                }
        }


        // - Check if the directory contains server.jar for import
        public bool IsImportValid(string path)
        {
            // Check mindustry-server.jar exists
            var exePath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(exePath);
        }


        // - Get Local server version
        public string GetLocalBuild()
        {
            // Get local version and build by mindustry_version.txt
            const string VERSION_TXT_FILE = "mindustry_version.txt"; // should contain something like "v125.1"
            var versionTxtFile = ServerPath.GetServersServerFiles(_serverData.ServerID, VERSION_TXT_FILE);
            if (!File.Exists(versionTxtFile))
            {
                Error = $"{VERSION_TXT_FILE} does not exist";
                return string.Empty;
            }
            var fileContents = File.ReadAllText(versionTxtFile);
            return $"{fileContents}";
        }


      
        // - Get Latest server version
        public async Task<string> GetRemoteBuild()
        {
            // Get latest version and build at https://github.com/Anuken/Mindustry/releases - (Using the API of course)
            try
            {
                using (var webClient = new WebClient())
                {
                    webClient.Headers.Add("user-agent", "I am WindowsGSM!"); // I was getting 403 Forbidden without this
                    var version = JObject.Parse(await webClient.DownloadStringTaskAsync("https://api.github.com/repos/Anuken/Mindustry/releases/latest"))["tag_name"].ToString(); // "v125.1"
                    return $"{version}"; // "v125.1"
                }
            }
            catch
            {
                Error = "Fail to get remote version and build";
                return string.Empty;
            }
        }
    }
}