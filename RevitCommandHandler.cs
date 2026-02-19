using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System.IO;

namespace ArchieCopilot
{
    public class RevitCommandHandler : IExternalEventHandler
    {
        private string? _pendingCode;
        private Action<bool, string>? _callback;
        private readonly ScriptEngine _pythonEngine;

        public RevitCommandHandler()
        {
            _pythonEngine = Python.CreateEngine();
            _pythonEngine.Runtime.LoadAssembly(typeof(Document).Assembly);
            _pythonEngine.Runtime.LoadAssembly(typeof(UIDocument).Assembly);
        }

        public void SetCode(string code, Action<bool, string> callback)
        {
            _pendingCode = code;
            _callback = callback;
        }

        public void Execute(UIApplication app)
        {
            if (_pendingCode == null || _callback == null)
                return;

            string code = _pendingCode;
            var callback = _callback;
            _pendingCode = null;
            _callback = null;

            try
            {
                var scope = _pythonEngine.CreateScope();

                // Provide Revit objects to the script
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    callback(false, "No active document. Please open a Revit project first.");
                    return;
                }

                scope.SetVariable("doc", doc);
                scope.SetVariable("uidoc", app.ActiveUIDocument);
                scope.SetVariable("uiapp", app);

                // Capture print output
                var outputStream = new MemoryStream();
                var writer = new StreamWriter(outputStream);
                _pythonEngine.Runtime.IO.SetOutput(outputStream, writer);
                _pythonEngine.Runtime.IO.SetErrorOutput(outputStream, writer);

                _pythonEngine.Execute(code, scope);
                writer.Flush();

                outputStream.Position = 0;
                string output = new StreamReader(outputStream).ReadToEnd();

                string result = string.IsNullOrWhiteSpace(output)
                    ? "Code executed successfully."
                    : output;

                callback(true, result);
            }
            catch (Exception ex)
            {
                callback(false, "Error: " + ex.Message);
            }
        }

        public string GetName() => "ArchieCopilot Code Executor";
    }
}
