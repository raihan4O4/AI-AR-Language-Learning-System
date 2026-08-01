using System;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Speech
{
    public sealed class AndroidTextToSpeechService : MonoBehaviour, ITextToSpeechService
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        enum InitializationState
        {
            NotStarted,
            Initializing,
            Ready,
            Failed
        }

        AndroidJavaObject m_TextToSpeech;
        AndroidJavaObject m_Activity;
        TextToSpeechInitListener m_InitListener;
        InitializationState m_InitializationState;
        VocabularyTranslation m_PendingTranslation;
#endif

        public void Speak(VocabularyTranslation translation)
        {
            if (translation == null)
                throw new ArgumentNullException(nameof(translation));

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EnsureInitialized())
            {
                m_PendingTranslation = translation;
                return;
            }

            SpeakWhenReady(translation);
#else
            Debug.Log($"TTS {translation.PronunciationLocale}: {translation.Text}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool EnsureInitialized()
        {
            if (m_InitializationState == InitializationState.Ready && m_TextToSpeech != null)
                return true;

            if (m_InitializationState == InitializationState.Initializing)
                return false;

            if (m_InitializationState == InitializationState.Failed)
            {
                Debug.LogWarning("Android Text-to-Speech is unavailable because initialization previously failed.");
                return false;
            }

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                m_Activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            if (m_Activity == null)
            {
                m_InitializationState = InitializationState.Failed;
                Debug.LogWarning("Android Text-to-Speech could not access the Unity activity.");
                return false;
            }

            m_InitializationState = InitializationState.Initializing;
            m_InitListener = new TextToSpeechInitListener(this);
            m_Activity.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        m_TextToSpeech = new AndroidJavaObject(
                            "android.speech.tts.TextToSpeech",
                            m_Activity,
                            m_InitListener);
                    }
                    catch (Exception exception)
                    {
                        m_InitializationState = InitializationState.Failed;
                        Debug.LogException(exception);
                    }
                }));
            return false;
        }

        void OnTextToSpeechInitialized(int status)
        {
            if (status != 0 || m_TextToSpeech == null)
            {
                m_InitializationState = InitializationState.Failed;
                m_PendingTranslation = null;
                m_InitListener = null;
                Debug.LogWarning($"Android Text-to-Speech initialization failed with status {status}.");
                return;
            }

            m_InitializationState = InitializationState.Ready;
            m_InitListener = null;
            var pending = m_PendingTranslation;
            m_PendingTranslation = null;
            if (pending != null)
                SpeakWhenReady(pending);
        }

        void SpeakWhenReady(VocabularyTranslation translation)
        {
            if (m_TextToSpeech == null || m_InitializationState != InitializationState.Ready)
                return;

            using (var locale = CreateLocale(translation.PronunciationLocale))
            {
                if (locale != null)
                {
                    var availability = m_TextToSpeech.Call<int>("setLanguage", locale);
                    if (availability < 0)
                    {
                        Debug.LogWarning(
                            $"Android Text-to-Speech language '{translation.PronunciationLocale}' is not installed or supported.");
                        return;
                    }
                }
            }

            var result = m_TextToSpeech.Call<int>(
                "speak",
                translation.Text,
                0,
                null,
                $"{translation.Language}-{Guid.NewGuid():N}");
            if (result < 0)
                Debug.LogWarning($"Android Text-to-Speech rejected '{translation.Text}' with result {result}.");
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

            var textToSpeech = m_TextToSpeech;
            m_TextToSpeech = null;
            m_InitializationState = InitializationState.NotStarted;
            m_PendingTranslation = null;
            m_InitListener = null;

            if (m_Activity != null)
            {
                m_Activity.Call(
                    "runOnUiThread",
                    new AndroidJavaRunnable(() =>
                    {
                        textToSpeech.Call("stop");
                        textToSpeech.Call("shutdown");
                        textToSpeech.Dispose();
                    }));
                m_Activity.Dispose();
                m_Activity = null;
            }
            else
            {
                textToSpeech.Dispose();
            }
        }

        sealed class TextToSpeechInitListener : AndroidJavaProxy
        {
            readonly AndroidTextToSpeechService m_Owner;

            public TextToSpeechInitListener(AndroidTextToSpeechService owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                m_Owner = owner;
            }

            public void onInit(int status)
            {
                m_Owner?.OnTextToSpeechInitialized(status);
            }
        }
#endif
    }
}
