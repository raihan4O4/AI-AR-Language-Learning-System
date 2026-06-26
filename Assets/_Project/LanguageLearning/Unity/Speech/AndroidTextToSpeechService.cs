using System;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Speech
{
    public sealed class AndroidTextToSpeechService : MonoBehaviour, ITextToSpeechService
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject m_TextToSpeech;
#endif

        public void Speak(VocabularyTranslation translation)
        {
            if (translation == null)
                throw new ArgumentNullException(nameof(translation));

#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureInitialized();

            using (var locale = CreateLocale(translation.PronunciationLocale))
            {
                if (locale != null)
                    m_TextToSpeech.Call<int>("setLanguage", locale);
            }

            m_TextToSpeech.Call<int>("speak", translation.Text, 0, null, $"{translation.Language}-{Guid.NewGuid():N}");
#else
            Debug.Log($"TTS {translation.PronunciationLocale}: {translation.Text}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void EnsureInitialized()
        {
            if (m_TextToSpeech != null)
                return;

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                m_TextToSpeech = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, null);
            }
        }

        static AndroidJavaObject CreateLocale(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                return null;

            var parts = localeCode.Split('-');
            if (parts.Length >= 2)
                return new AndroidJavaObject("java.util.Locale", parts[0], parts[1]);

            return new AndroidJavaObject("java.util.Locale", localeCode);
        }

        void OnDestroy()
        {
            if (m_TextToSpeech == null)
                return;

            m_TextToSpeech.Call("stop");
            m_TextToSpeech.Call("shutdown");
            m_TextToSpeech.Dispose();
            m_TextToSpeech = null;
        }
#endif
    }
}
