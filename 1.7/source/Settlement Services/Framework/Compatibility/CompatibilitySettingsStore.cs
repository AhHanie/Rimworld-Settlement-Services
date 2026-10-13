using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Compatibility
{
    public class CompatibilitySettingsStore : IExposable
    {
        private Dictionary<string, bool> boolValues = new Dictionary<string, bool>();
        private Dictionary<string, int> intValues = new Dictionary<string, int>();
        private Dictionary<string, float> floatValues = new Dictionary<string, float>();

        public bool GetBool(string key, bool defaultValue) => boolValues.TryGetValue(key, out bool value) ? value : defaultValue;
        public void SetBool(string key, bool value) => boolValues[key] = value;

        public int GetInt(string key, int defaultValue) => intValues.TryGetValue(key, out int value) ? value : defaultValue;
        public void SetInt(string key, int value) => intValues[key] = value;

        public float GetFloat(string key, float defaultValue) => floatValues.TryGetValue(key, out float value) ? value : defaultValue;
        public void SetFloat(string key, float value) => floatValues[key] = value;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref boolValues, "boolValues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref intValues, "intValues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref floatValues, "floatValues", LookMode.Value, LookMode.Value);

            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            if (boolValues == null) boolValues = new Dictionary<string, bool>();
            if (intValues == null) intValues = new Dictionary<string, int>();
            if (floatValues == null) floatValues = new Dictionary<string, float>();
        }
    }
}
