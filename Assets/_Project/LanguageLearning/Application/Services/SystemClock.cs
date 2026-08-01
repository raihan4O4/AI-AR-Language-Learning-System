using System;
using AIAR.LanguageLearning.Application.Ports;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
