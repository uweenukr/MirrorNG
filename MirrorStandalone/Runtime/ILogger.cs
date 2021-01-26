using System;

namespace Mirror
{

    //Based on: https://docs.unity3d.com/ScriptReference/LogType.html
    public enum LogType
    {
        Error,
        Assert,
        Warning,
        Log,
        Exception,
    }

    public interface ILogger
    {
        bool IsLogTypeAllowed(LogType logType);

        void Log(object message);

        void LogWarning(ILogger logger, object message);

        void LogError(ILogger logger, object message);

        void LogException(Exception ex);
    }

    public class StandaloneLogger : ILogger
    {
        public LogType filterLogType;

        public bool IsLogTypeAllowed(LogType logType)
        {
            throw new NotImplementedException();
        }

        public void Log(object message)
        {
            throw new NotImplementedException();
        }

        public void LogError(ILogger logger, object message)
        {
            throw new NotImplementedException();
        }

        public void LogException(Exception ex)
        {
            throw new NotImplementedException();
        }

        public void LogWarning(ILogger logger, object message)
        {
            throw new NotImplementedException();
        }
    }
}
