using System;
using System.Runtime.CompilerServices;

namespace Fusion.GKeys
{
	public static class Debug
	{
		static Logger m_logger = new Logger("Debug", "./Logs/Debug.log");

		public static void Info<T>(T data)
		{
			m_logger.Info(data);
		}

		public static void Success<T>(T data)
		{
			m_logger.Success(data);
		}

		public static void Warning<T>(T data)
		{
			m_logger.Warning(data);
		}

		public static void Error<T>(T data)
		{
			m_logger.Error(data);
		}

		public static void Exception(Exception ex,
			bool full = false,
			[CallerMemberName]  string CallerName = "",
			[CallerFilePath]    string CallerFilePath = "",
			[CallerLineNumber]  int CallerLineNumber = -1)
		{
			m_logger.Exception(ex, full, CallerName, CallerFilePath, CallerLineNumber);
		}

#if DEBUG
		public static void Assert(bool expression, string text = null)
		{
			if (!expression)
			{
				System.Diagnostics.Debugger.Break();
			}
		}

		public static void Break()
		{
			System.Diagnostics.Debugger.Break();
		}
#else
		public static void Assert(bool expression, string text = null)
		{	}
		public static void Break()
		{	}
#endif


	}
}
