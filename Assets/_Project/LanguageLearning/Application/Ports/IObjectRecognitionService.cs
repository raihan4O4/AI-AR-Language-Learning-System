using System;
using System.Collections.Generic;
using AIAR.LanguageLearning.Application.Models;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface IObjectRecognitionService
    {
        event Action<ObjectRecognitionResult> ObjectRecognized;

        event Action<IReadOnlyList<ObjectRecognitionResult>> ObjectRecognitionsUpdated;

        event Action<RecognitionServiceStatus> StatusChanged;

        bool IsRunning { get; }

        void StartRecognition();

        void StopRecognition();
    }
}
