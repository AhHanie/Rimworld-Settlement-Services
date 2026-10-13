using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI.Audio
{
    public static class ServiceSoundtrackController
    {
        private const float CrossfadeSeconds = 2f;
        private const float MusicSilenceKeepAliveSeconds = 3f;
        private const float VanillaSilenceHoldSeconds = 999999f;

        private static Dialog_SettlementServices activeDialog;
        private static AudioSource audioSource;
        private static SongDef playingTrack;
        private static ServiceSoundtrackFade activeFade;

        public static void OnDialogOpened(Dialog_SettlementServices dialog)
        {
            CancelPendingFade();
            activeDialog = dialog;
            StartTrackIfEnabled();
        }

        public static void OnDialogClosed(Dialog_SettlementServices dialog)
        {
            if (activeDialog != dialog) return;
            activeDialog = null;
            CancelPendingFade();
            FadeOutAndRelease();
        }

        public static void RefreshForSettingsChange()
        {
            if (activeDialog == null) return;

            if (!ModSettings.Current.soundtrackEnabled)
            {
                CancelPendingFade();
                FadeOutAndRelease();
            }
            else if (playingTrack == null)
            {
                CancelPendingFade();
                StartTrackIfEnabled();
            }
        }

        private static void StartTrackIfEnabled()
        {
            SongDef song = ResolveTrack(activeDialog);
            playingTrack = song;
            if (song?.clip == null) return;

            EnsureAudioSource();
            audioSource.clip = song.clip;
            audioSource.volume = 0f;
            audioSource.Play();

            Find.MusicManagerPlay?.ForceFadeoutAndSilenceFor(VanillaSilenceHoldSeconds, CrossfadeSeconds, preventDangerTransition: true);
            StartFade(0f, song.volume * Prefs.VolumeMusic, null);
        }

        private static void FadeOutAndRelease()
        {
            if (playingTrack == null) return;

            Find.MusicManagerPlay?.ForceFadeoutAndSilenceFor(MusicSilenceKeepAliveSeconds, CrossfadeSeconds);
            playingTrack = null;

            StartFade(audioSource.volume, 0f, () =>
            {
                audioSource.Stop();
                UnityEngine.Object.Destroy(audioSource.gameObject);
                audioSource = null;
                Find.MusicManagerPlay?.ScheduleNewSong();
            });
        }

        private static SongDef ResolveTrack(Dialog_SettlementServices dialog)
        {
            if (!ModSettings.Current.soundtrackEnabled || dialog == null) return null;

            TechLevel level = dialog.Settlement?.Faction?.def.techLevel ?? TechLevel.Undefined;
            if (level == TechLevel.Undefined) return null;
            return DefDatabase<SongDef>.GetNamedSilentFail("SettlementServicesSoundtrack_" + level);
        }

        private static void EnsureAudioSource()
        {
            if (audioSource != null) return;
            var gameObject = new GameObject("SettlementServicesSoundtrackAudioSourceDummy");
            gameObject.transform.parent = Find.Root.soundRoot.sourcePool.sourcePoolCamera.cameraSourcesContainer.transform;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.bypassEffects = true;
            audioSource.bypassListenerEffects = true;
            audioSource.bypassReverbZones = true;
            audioSource.spatialBlend = 0f;
            audioSource.priority = 0;
        }

        private static void StartFade(float from, float to, Action onComplete)
        {
            ServiceSoundtrackFade fade = audioSource.gameObject.AddComponent<ServiceSoundtrackFade>();
            activeFade = fade;
            fade.Begin(audioSource, from, to, CrossfadeSeconds, () =>
            {
                if (activeFade == fade) activeFade = null;
                onComplete?.Invoke();
            });
        }

        private static void CancelPendingFade()
        {
            if (activeFade == null) return;
            UnityEngine.Object.Destroy(activeFade);
            activeFade = null;
        }
    }
}
