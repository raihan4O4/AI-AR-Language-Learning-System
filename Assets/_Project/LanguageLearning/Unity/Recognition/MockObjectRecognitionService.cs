using System;
using System.Collections;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Recognition
{
    public sealed class MockObjectRecognitionService : MonoBehaviour, IObjectRecognitionService
    {
        [SerializeField]
        string[] m_Labels =
        {
            "book",
            "bottle",
            "chair",
            "laptop",
            "phone"
        };

        [SerializeField]
        float m_IntervalSeconds = 2.5f;

        [SerializeField]
        bool m_StartOnEnable;

        Coroutine m_RecognitionLoop;
        int m_CurrentIndex;

        public event Action<ObjectRecognitionResult> ObjectRecognized;

        public bool IsRunning => m_RecognitionLoop != null;

        void OnEnable()
        {
            if (m_StartOnEnable)
                StartRecognition();
        }

        void OnDisable()
        {
            StopRecognition();
        }

        public void StartRecognition()
        {
            if (IsRunning)
                return;

            m_RecognitionLoop = StartCoroutine(RecognitionLoop());
        }

        public void StopRecognition()
        {
            if (m_RecognitionLoop == null)
                return;

            StopCoroutine(m_RecognitionLoop);
            m_RecognitionLoop = null;
        }

        IEnumerator RecognitionLoop()
        {
            while (true)
            {
                if (m_Labels != null && m_Labels.Length > 0)
                {
                    var label = m_Labels[m_CurrentIndex % m_Labels.Length];
                    var boundingBox = CreateMockBoundingBox(m_CurrentIndex);
                    m_CurrentIndex++;
                    ObjectRecognized?.Invoke(new ObjectRecognitionResult(label, 0.95f, "mock", boundingBox));
                }

                yield return new WaitForSeconds(Mathf.Max(0.25f, m_IntervalSeconds));
            }
        }

        static NormalizedBoundingBox CreateMockBoundingBox(int index)
        {
            var column = index % 3;
            var row = (index / 3) % 2;
            return new NormalizedBoundingBox(
                0.16f + (column * 0.22f),
                0.22f + (row * 0.22f),
                0.34f,
                0.26f);
        }
    }
}
