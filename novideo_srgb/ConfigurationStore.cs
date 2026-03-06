using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace novideo_srgb
{
    public sealed class NovideoConfiguration
    {
        public uint HotkeyModifiers { get; set; }

        public uint HotkeyKey { get; set; }

        public List<MonitorConfiguration> Monitors { get; } = new List<MonitorConfiguration>();
    }

    public sealed class MonitorConfiguration
    {
        public string Path { get; set; }

        public bool ClampSdr { get; set; }

        public int SelectedProfileIndex { get; set; }

        public List<MonitorProfile> Profiles { get; } = new List<MonitorProfile>();
    }

    public static class ConfigurationStore
    {
        private const int ProfileCount = 3;

        public static NovideoConfiguration Load(string path)
        {
            var configuration = new NovideoConfiguration();
            if (!File.Exists(path))
            {
                return configuration;
            }

            var root = XElement.Load(path);
            configuration.HotkeyModifiers = ParseUInt(root.Attribute("hotkey_modifiers"), 0);
            configuration.HotkeyKey = ParseUInt(root.Attribute("hotkey_key"), 0);

            foreach (var monitorElement in root.Elements("monitor"))
            {
                var monitorConfiguration = new MonitorConfiguration
                {
                    Path = (string)monitorElement.Attribute("path"),
                    ClampSdr = ParseBool(monitorElement.Attribute("clamp_sdr"), false),
                    SelectedProfileIndex = ParseInt(monitorElement.Attribute("selected_profile"), 0),
                };

                if (string.IsNullOrWhiteSpace(monitorConfiguration.Path))
                {
                    continue;
                }

                var profileElements = monitorElement.Elements("profile").ToList();
                if (profileElements.Count == 0)
                {
                    monitorConfiguration.Profiles.Add(CreateLegacyProfile(monitorElement, 0));
                }
                else
                {
                    for (var i = 0; i < profileElements.Count && i < ProfileCount; i++)
                    {
                        monitorConfiguration.Profiles.Add(CreateProfile(profileElements[i], i));
                    }
                }

                NormalizeProfiles(monitorConfiguration.Profiles);
                monitorConfiguration.SelectedProfileIndex =
                    ClampIndex(monitorConfiguration.SelectedProfileIndex, monitorConfiguration.Profiles.Count);
                configuration.Monitors.Add(monitorConfiguration);
            }

            return configuration;
        }

        public static void Save(string path, NovideoConfiguration configuration)
        {
            var root = new XElement("monitors",
                new XAttribute("hotkey_modifiers", configuration.HotkeyModifiers),
                new XAttribute("hotkey_key", configuration.HotkeyKey),
                configuration.Monitors.Select(CreateMonitorElement));
            root.Save(path);
        }

        private static XElement CreateMonitorElement(MonitorConfiguration configuration)
        {
            var profiles = configuration.Profiles.Select((profile, index) => profile.CloneForIndex(index)).ToList();
            NormalizeProfiles(profiles);

            var selectedProfileIndex = ClampIndex(configuration.SelectedProfileIndex, profiles.Count);
            var currentProfile = profiles[selectedProfileIndex];

            return new XElement("monitor",
                new XAttribute("path", configuration.Path ?? ""),
                new XAttribute("clamp_sdr", configuration.ClampSdr),
                new XAttribute("selected_profile", selectedProfileIndex),
                new XAttribute("use_icc", currentProfile.UseIcc),
                new XAttribute("icc_path", currentProfile.ProfilePath ?? ""),
                new XAttribute("calibrate_gamma", currentProfile.CalibrateGamma),
                new XAttribute("selected_gamma", currentProfile.SelectedGamma),
                new XAttribute("custom_gamma", currentProfile.CustomGamma),
                new XAttribute("custom_percentage", currentProfile.CustomPercentage),
                new XAttribute("target", currentProfile.Target),
                new XAttribute("disable_optimization", currentProfile.DisableOptimization),
                profiles.Select(CreateProfileElement));
        }

        private static XElement CreateProfileElement(MonitorProfile profile)
        {
            return new XElement("profile",
                new XAttribute("name", profile.DisplayName),
                new XAttribute("use_icc", profile.UseIcc),
                new XAttribute("icc_path", profile.ProfilePath ?? ""),
                new XAttribute("calibrate_gamma", profile.CalibrateGamma),
                new XAttribute("selected_gamma", profile.SelectedGamma),
                new XAttribute("custom_gamma", profile.CustomGamma),
                new XAttribute("custom_percentage", profile.CustomPercentage),
                new XAttribute("target", profile.Target),
                new XAttribute("disable_optimization", profile.DisableOptimization),
                new XAttribute("dither_state", profile.DitherState),
                new XAttribute("dither_mode", profile.DitherMode),
                new XAttribute("dither_bits", profile.DitherBits));
        }

        private static MonitorProfile CreateLegacyProfile(XElement element, int index)
        {
            return new MonitorProfile(index)
            {
                UseIcc = ParseBool(element.Attribute("use_icc"), false),
                ProfilePath = (string)element.Attribute("icc_path") ?? "",
                CalibrateGamma = ParseBool(element.Attribute("calibrate_gamma"), false),
                SelectedGamma = ParseInt(element.Attribute("selected_gamma"), 0),
                CustomGamma = ParseDouble(element.Attribute("custom_gamma"), 2.2),
                CustomPercentage = ParseDouble(element.Attribute("custom_percentage"), 100),
                Target = ParseInt(element.Attribute("target"), 0),
                DisableOptimization = ParseBool(element.Attribute("disable_optimization"), false),
            };
        }

        private static MonitorProfile CreateProfile(XElement element, int index)
        {
            return new MonitorProfile(index)
            {
                Name = (string)element.Attribute("name"),
                UseIcc = ParseBool(element.Attribute("use_icc"), false),
                ProfilePath = (string)element.Attribute("icc_path") ?? "",
                CalibrateGamma = ParseBool(element.Attribute("calibrate_gamma"), false),
                SelectedGamma = ParseInt(element.Attribute("selected_gamma"), 0),
                CustomGamma = ParseDouble(element.Attribute("custom_gamma"), 2.2),
                CustomPercentage = ParseDouble(element.Attribute("custom_percentage"), 100),
                Target = ParseInt(element.Attribute("target"), 0),
                DisableOptimization = ParseBool(element.Attribute("disable_optimization"), false),
                DitherState = ParseInt(element.Attribute("dither_state"), 0),
                DitherMode = ParseInt(element.Attribute("dither_mode"), 0),
                DitherBits = ParseInt(element.Attribute("dither_bits"), 0),
            };
        }

        private static void NormalizeProfiles(IList<MonitorProfile> profiles)
        {
            while (profiles.Count > ProfileCount)
            {
                profiles.RemoveAt(profiles.Count - 1);
            }

            if (profiles.Count == 0)
            {
                profiles.Add(new MonitorProfile(0));
            }

            var seedProfile = profiles[0].CloneForIndex(0);
            while (profiles.Count < ProfileCount)
            {
                var clone = seedProfile.CloneForIndex(profiles.Count);
                clone.Name = MonitorProfile.GetDefaultName(profiles.Count);
                profiles.Add(clone);
            }
        }

        private static int ClampIndex(int value, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (value < 0)
            {
                return 0;
            }

            if (value >= count)
            {
                return count - 1;
            }

            return value;
        }

        private static bool ParseBool(XAttribute attribute, bool defaultValue)
        {
            bool parsedValue;
            return attribute != null && bool.TryParse(attribute.Value, out parsedValue) ? parsedValue : defaultValue;
        }

        private static int ParseInt(XAttribute attribute, int defaultValue)
        {
            int parsedValue;
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out parsedValue)
                ? parsedValue
                : defaultValue;
        }

        private static uint ParseUInt(XAttribute attribute, uint defaultValue)
        {
            uint parsedValue;
            return attribute != null && uint.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out parsedValue)
                ? parsedValue
                : defaultValue;
        }

        private static double ParseDouble(XAttribute attribute, double defaultValue)
        {
            double parsedValue;
            return attribute != null &&
                   double.TryParse(attribute.Value, NumberStyles.Float | NumberStyles.AllowThousands,
                       CultureInfo.InvariantCulture, out parsedValue)
                ? parsedValue
                : defaultValue;
        }
    }
}
