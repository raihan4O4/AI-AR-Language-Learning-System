using System;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
