using System;
using AIAR.LanguageLearning.Application.Models;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface IObjectRecognitionService
    {
        event Action<ObjectRecognitionResult> ObjectRecognized;

        bool IsRunning { get; }

        void StartRecognition();

        void StopRecognition();
    }
}
