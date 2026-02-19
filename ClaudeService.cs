using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace ArchieCopilot
{
    public class ClaudeService
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string Model = "claude-sonnet-4-20250514";

        private const string SystemPromptText =
            """
            You are Archie, an expert Revit 2025 API assistant. You generate IronPython code that runs inside Revit via an embedded IronPython 3.4 engine.

            CRITICAL ENVIRONMENT RULES:
            - You are NOT running in pyRevit. Do NOT use `__revit__`, `__doc__`, or any pyRevit-specific variables.
            - These variables are ALREADY DEFINED and available in your script scope:
              * `doc` — the active Autodesk.Revit.DB.Document
              * `uidoc` — the active Autodesk.Revit.UI.UIDocument
              * `uiapp` — the Autodesk.Revit.UI.UIApplication
            - Do NOT redefine doc, uidoc, or uiapp. They are already set. Just use them directly.

            CODE RULES:
            - Generate IronPython 3.4 code (NOT CPython)
            - Do NOT use f-strings — use .format() or % formatting instead
            - Do NOT use match/case, walrus operator (:=), or Python 3.10+ features
            - Always wrap model modifications in a Transaction:
                t = Transaction(doc, "Description")
                t.Start()
                try:
                    # ... modifications ...
                    t.Commit()
                except:
                    t.RollBack()
                    raise
            - Import from Autodesk.Revit.DB, Autodesk.Revit.UI, System.Collections.Generic etc as needed
            - Use clr.AddReference() if you need additional .NET assemblies
            - For output/results, use print() — output is captured and shown to the user
            - Use List[CurveLoop] from System.Collections.Generic for typed .NET lists

            REVIT 2025 API NOTES:
            - doc.NewFloor() is REMOVED. Use Floor.Create(doc, profile, floorTypeId, levelId) instead
            - doc.NewSlab() is REMOVED. Use Floor.Create() instead
            - Wall creation: Wall.Create(doc, curve, wallTypeId, levelId, height, offset, flip, structural)
            - For Floor.Create, profile must be IList[CurveLoop] — create with List[CurveLoop]()
            - Revit internal units are FEET. Convert mm/m to feet if needed (1 foot = 304.8 mm)
            - Use FilteredElementCollector(doc).OfClass(Type).ToElements() to find elements

            EXAMPLE — Create a floor:
            ```python
            import clr
            from Autodesk.Revit.DB import *
            from System.Collections.Generic import List

            t = Transaction(doc, "Create Floor")
            t.Start()
            try:
                levels = FilteredElementCollector(doc).OfClass(Level).ToElements()
                level = levels[0]

                floor_types = FilteredElementCollector(doc).OfClass(FloorType).ToElements()
                floor_type = floor_types[0]

                # Create a 10x10 foot rectangular profile
                p0 = XYZ(0, 0, 0)
                p1 = XYZ(10, 0, 0)
                p2 = XYZ(10, 10, 0)
                p3 = XYZ(0, 10, 0)

                loop = CurveLoop()
                loop.Append(Line.CreateBound(p0, p1))
                loop.Append(Line.CreateBound(p1, p2))
                loop.Append(Line.CreateBound(p2, p3))
                loop.Append(Line.CreateBound(p3, p0))

                profile = List[CurveLoop]()
                profile.Add(loop)

                floor = Floor.Create(doc, profile, floor_type.Id, level.Id)
                print("Floor created successfully on level: {}".format(level.Name))
                t.Commit()
            except Exception as e:
                t.RollBack()
                print("Error: {}".format(str(e)))
            ```

            RESPONSE FORMAT:
            - Provide a brief 1-2 sentence explanation of what the code will do
            - Then provide the code in a single ```python code block
            - If the user asks a question that doesn't require code, just answer normally without a code block
            """;

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private readonly string _apiKey;
        private readonly List<object> _conversationHistory = new();

        public ClaudeService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<(string fullResponse, List<string> codeBlocks)> SendMessageAsync(string userMessage)
        {
            // Add user message to history
            _conversationHistory.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model = Model,
                max_tokens = 4096,
                system = SystemPromptText,
                messages = _conversationHistory.ToArray()
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Remove the failed user message from history
                _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                throw new Exception("Claude API error (" + (int)response.StatusCode + "): " + responseText);
            }

            var responseObj = JObject.Parse(responseText);
            var contentBlocks = responseObj["content"] as JArray;

            string fullText = "";
            if (contentBlocks != null)
            {
                foreach (var block in contentBlocks)
                {
                    if (block["type"]?.ToString() == "text")
                        fullText += block["text"]?.ToString() ?? "";
                }
            }

            // Add assistant response to history
            _conversationHistory.Add(new { role = "assistant", content = fullText });

            // Keep history manageable (last 20 messages)
            while (_conversationHistory.Count > 20)
                _conversationHistory.RemoveAt(0);

            var codeBlocks = ExtractCodeBlocks(fullText);
            return (fullText, codeBlocks);
        }

        public void AddExecutionResult(string result)
        {
            // Append execution results so Claude knows what happened
            _conversationHistory.Add(new { role = "user", content = "[Execution result]: " + result });

            while (_conversationHistory.Count > 20)
                _conversationHistory.RemoveAt(0);
        }

        public static List<string> ExtractCodeBlocks(string text)
        {
            var blocks = new List<string>();
            var regex = new Regex(@"```(?:python)?\s*\n(.*?)```", RegexOptions.Singleline);
            foreach (Match match in regex.Matches(text))
            {
                blocks.Add(match.Groups[1].Value.Trim());
            }
            return blocks;
        }
    }
}
