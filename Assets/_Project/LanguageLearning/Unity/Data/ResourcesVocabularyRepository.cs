using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Data
{
    public sealed class ResourcesVocabularyRepository : MonoBehaviour, IVocabularyRepository
    {
        [SerializeField]
        TextAsset m_CatalogAsset;

        [SerializeField]
        string m_ResourcesPath = "Vocabulary/vocabulary_catalog";

        IReadOnlyList<VocabularyEntry> m_Entries;

        public IReadOnlyList<VocabularyEntry> GetAll()
        {
            EnsureLoaded();
            return m_Entries;
        }

        public bool TryGetById(string id, out VocabularyEntry entry)
        {
            EnsureLoaded();

            entry = m_Entries.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

            return entry != null;
        }

        public void ClearCache()
        {
            m_Entries = null;
        }

        void EnsureLoaded()
        {
            if (m_Entries != null)
                return;

            var catalogAsset = m_CatalogAsset;
            if (catalogAsset == null && !string.IsNullOrWhiteSpace(m_ResourcesPath))
                catalogAsset = Resources.Load<TextAsset>(m_ResourcesPath);

            if (catalogAsset == null)
                throw new InvalidOperationException($"Vocabulary catalog not found at Resources/{m_ResourcesPath}.");

            var document = JsonUtility.FromJson<VocabularyCatalogDocument>(catalogAsset.text);
            if (document == null || document.items == null || document.items.Length == 0)
                throw new InvalidOperationException("Vocabulary catalog is empty or invalid.");

            m_Entries = document.items
                .Where(item => item != null)
                .Select(ToDomain)
                .ToArray();
        }

        static VocabularyEntry ToDomain(VocabularyItemDocument item)
        {
            if (item.translations == null || item.translations.Length == 0)
                throw new InvalidOperationException($"Vocabulary item '{item.id}' has no translations.");

            var translations = item.translations.Select(translation =>
            {
                if (!LanguageCodeExtensions.TryParse(translation.language, out var language))
                    throw new InvalidOperationException($"Unsupported language '{translation.language}' in vocabulary item '{item.id}'.");

                return new VocabularyTranslation(
                    language,
                    translation.text,
                    translation.phoneticHint,
                    translation.pronunciationLocale);
            });

            return new VocabularyEntry(
                item.id,
                item.canonicalObjectLabel,
                item.category,
                item.recognitionLabels,
                translations);
        }

        [Serializable]
        sealed class VocabularyCatalogDocument
        {
            public string schemaVersion = string.Empty;
            public VocabularyItemDocument[] items = Array.Empty<VocabularyItemDocument>();
        }

        [Serializable]
        sealed class VocabularyItemDocument
        {
            public string id = string.Empty;
            public string canonicalObjectLabel = string.Empty;
            public string category = string.Empty;
            public string[] recognitionLabels = Array.Empty<string>();
            public TranslationDocument[] translations = Array.Empty<TranslationDocument>();
        }

        [Serializable]
        sealed class TranslationDocument
        {
            public string language = string.Empty;
            public string text = string.Empty;
            public string phoneticHint = string.Empty;
            public string pronunciationLocale = string.Empty;
        }
    }
}
