using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace XI.Host.Common
{
    public static class Global
    {
        private class SettingsContainer
        {
            public string LogType { get; set; }
            public string LogVerbosity { get; set; }
            public ushort ServerPopulationLimit { get; set; }
            public int DelayCharacterCreatedResponse { get; set; }
            public ushort ClientReceiveBufferSize { get; set; }
            public int TcpServerBacklog { get; set; }
            public int CleanUpSessionsFrequency { get; set; }
        }

        private class HostContainer
        {
            public string ServerSettingsDirectory { get; set; }
            public SettingsContainer Host { get; set; }
        }

        // Match A1_VAL = "str", A2_VAL = 123, A3_VAL = 0.5, and --A4_VAL = "commented"
        // TODO: negative look-ahead (?!-) or [^-] are not working
        private static readonly Regex regexLuaKeyValue = new Regex("([A-Z0-9_]+)\\s+=\\s+(\".*\"|[0-9\\.]+|false|true)", RegexOptions.Compiled);

        public static Dictionary<string, string> Config = new Dictionary<string, string>();

        static Global()
        {
            Console.TreatControlCAsInput = true;

            // Validates data based on container data type.
            HostContainer container = JsonConvert.DeserializeObject<HostContainer>(File.ReadAllText("host.json"));

            // TODO use reflection to fill these automatically
            Config.Add("LogType", container.Host.LogType);
            Config.Add("LogVerbosity", container.Host.LogVerbosity);
            Config.Add("ServerPopulationLimit", container.Host.ServerPopulationLimit.ToString());
            Config.Add("DelayCharacterCreatedResponse", container.Host.DelayCharacterCreatedResponse.ToString());
            Config.Add("ClientReceiveBufferSize", container.Host.ClientReceiveBufferSize.ToString());
            Config.Add("TcpServerBacklog", container.Host.TcpServerBacklog.ToString());
            Config.Add("CleanUpSessionsFrequency", container.Host.CleanUpSessionsFrequency.ToString());

            foreach (var dir in new string[] { container.ServerSettingsDirectory, container.ServerSettingsDirectory + Path.DirectorySeparatorChar + "default"})
            {
                if (!Directory.Exists(dir))
                {
                    throw new DirectoryNotFoundException($"The directory '{dir}' does not exist.  Make sure that it refers to a location where the Server configuration files exist.  This software depends on configurations that have previously been setup.");
                }

                foreach (var lua in Directory.GetFiles(dir, "*.lua", SearchOption.TopDirectoryOnly))
                {
                    foreach (var line in File.ReadAllLines(lua))
                    {
                        // Ignore commented lines (addresses negative look-ahead issue.
                        if (line.Trim(' ').StartsWith("--"))
                            continue;

                        Match match = regexLuaKeyValue.Match(line);

                        if (match.Success)
                        {
                            string key = match.Groups[1].Value;
                            string value = match.Groups[2].Value.Trim('"');

                            if (!Config.ContainsKey(key))
                                Config.Add(key, value);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetConfigAsBoolean(string key)
        {
            if (int.TryParse(Config[key], out int value))
            {
                return value != 0;
            }

            return bool.Parse(Config[key]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetConfigAsUShort(string key)
        {
            return ushort.Parse(Config[key]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetConfigAsInt32(string key)
        {
            return int.Parse(Config[key]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetConfigAsUInt32(string key)
        {
            return uint.Parse(Config[key]);
        }
    }
}
