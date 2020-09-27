using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace Fusion.GKeys
{
	public enum ELogType
	{
		Info,
		Success,
		Warning,
		Error,
		Exception
	}

	public class Logger : IDisposable
	{
#if DEBUG
		const bool k_IsDebug = true;
#else
		const bool k_IsDebug = false;
#endif
		static readonly Dictionary<ELogType, ConsoleColor> _TypeConsoleColor = new Dictionary<ELogType, ConsoleColor>()
		{
			{ ELogType.Info, ConsoleColor.White },
			{ ELogType.Success, ConsoleColor.Green },
			{ ELogType.Warning, ConsoleColor.Yellow },
			{ ELogType.Error, ConsoleColor.Red },
			{ ELogType.Exception, ConsoleColor.Magenta },
		};
		static readonly Dictionary<ELogType, string> _TypeToText = new Dictionary<ELogType, string>()
		{
			{ ELogType.Info, "" },
			{ ELogType.Success, "" },
			{ ELogType.Warning, "" },
			{ ELogType.Error, "" },
			{ ELogType.Exception, "" },
		};

		public struct Settings
		{
			public string filePath;
			public bool? consoleOutput;
			public bool? appendFile;
		}


		protected StreamWriter streamWriter;
		public string Name { get; protected set; }
		public bool ConsoleOutput { get; protected set; }

		public Logger(string name, in Settings settings)
		{
			Init(name, settings.filePath ?? name, settings.consoleOutput ?? k_IsDebug, settings.appendFile ?? false);
		}

		public Logger(string name, string filePath)
		{
			Init(name, filePath, k_IsDebug, false);
		}

		protected void Init(string name, string filePath, bool consoleOutput, bool appendFile)
		{
			if (filePath == null)
			{
				throw new ArgumentNullException(nameof(filePath));
			}

			Name = name ?? "System";
			ConsoleOutput = consoleOutput;
			var ext = Path.GetExtension(filePath);
			if (ext == null || ext == string.Empty)
			{
				filePath += ".log";
			}

			if (!appendFile && File.Exists(filePath))
			{
				File.Move(filePath, filePath + ".old", true);
			}

			Directory.CreateDirectory(new FileInfo(filePath).Directory.FullName);
			FileMode fileMode = appendFile ? FileMode.Append : FileMode.Create;
			streamWriter = new StreamWriter(File.Open(filePath, fileMode, FileAccess.Write, FileShare.Read))
			{
				AutoFlush = true
			};
		}

		protected void LogImpl(string text, ELogType type)
		{
			var output = $"[{Name} {DateTime.Now.ToString("dd.MM HH:mm:ss")}]{ _TypeToText[type]}: {text}";
			if (ConsoleOutput)
			{
				Console.ForegroundColor = _TypeConsoleColor[type];
				Console.WriteLine(output);
				Console.ResetColor();
			}
#if DEBUG
			System.Diagnostics.Debug.WriteLine(output);
#endif
			streamWriter.WriteLine(output);

		}

		public void Info<T>(T obj)
		{
			LogImpl(obj.ToString(), ELogType.Info);
		}


		public void Success<T>(T obj)
		{
			LogImpl(obj.ToString(), ELogType.Success);
		}


		public void Warning<T>(T obj)
		{
			LogImpl(obj.ToString(), ELogType.Warning);
		}

		public void Error<T>(T obj)
		{
			LogImpl(obj.ToString(), ELogType.Error);
		}


		public void Exception(
			Exception ex,
			bool full = false,
			[CallerMemberName]  string CallerName = "",
			[CallerFilePath]        string CallerFilePath = "",
			[CallerLineNumber]  int CallerLineNumber = -1)
		{
			LogImpl(string.Format("[{0}:{1}:{2}] {3}", CallerFilePath, CallerName, CallerLineNumber, (full ? ex.ToString() : ex.Message)), ELogType.Exception);
		}

		#region IDisposable Support
		
		protected virtual void Dispose(bool disposing)
		{
			if (streamWriter != null)
			{
				if (disposing)
				{
					streamWriter.Dispose();
				}

				streamWriter = null;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Logger() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			
			Dispose(true);
			
		}
		#endregion


	}
}
