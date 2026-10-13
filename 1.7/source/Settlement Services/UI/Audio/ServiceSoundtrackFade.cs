using System;
using UnityEngine;

namespace Settlement_Services.UI.Audio
{
    public class ServiceSoundtrackFade : MonoBehaviour
    {
        private AudioSource source;
        private float from;
        private float to;
        private float duration;
        private float elapsed;
        private Action onComplete;

        public void Begin(AudioSource audioSource, float startVolume, float targetVolume, float fadeDuration, Action completeCallback)
        {
            source = audioSource;
            from = startVolume;
            to = targetVolume;
            duration = fadeDuration;
            elapsed = 0f;
            onComplete = completeCallback;
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(from, to, t);

            if (t >= 1f)
            {
                onComplete?.Invoke();
                Destroy(this);
            }
        }
    }
}
