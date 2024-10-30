namespace clioAgent.PgLogParser;

public record PgObjectInstance(long Position, string OtherData);


public class PgParser {

	private readonly List<string> _unprocessedObjects;
	private readonly List<string> _processedObjects;
	private int _processedCount = 0;
	private int _totalCount = 0;
	
	public event EventHandler<int>? ProgressChanged;
	
	public PgParser(){
		_unprocessedObjects = [];
		_processedObjects = [];
	}
	public List<PgObjectInstance> Init(List<string> lines){
		List<PgObjectInstance> result = [];
		lines.ToList().ForEach(line=> {
			if(ParseObjectListLine(line) is { } obj){
				result.Add(obj);
			}
		});
		_unprocessedObjects.AddRange(result.Select(x => x.OtherData));
		_totalCount = _unprocessedObjects.Count;
		return result;
	}
	
	private static PgObjectInstance? ParseObjectListLine(string line){
		if(line.Contains(';')){
			string[] parts = line.Split(';');
			bool isLong = long.TryParse(parts[0].Trim(), out long position);
			if(isLong){
				var items = parts[1].Trim().Split(' ');
				string interesting = string.Join(' ', items[2..])
					.Replace(" - "," ")
					.Replace("puser","") //Who is puser ?
					.Replace("public","")
					.Replace(" dlv ","")
					.Replace("\"","")
					.Replace("  "," ")
					.Trim();
				return new PgObjectInstance(position, interesting);
			}
		}
		return null;
	}
	
	public void Process(string logLine){
		
		// const string lastLinemarker = "pg_restore: finished main parallel loop";
		// if(logLine.Contains(lastLinemarker)){
		// 	var a = "";
		// }
		
		string[] marker = [
			"pg_restore: processing item",
			"pg_restore: launching item",
			"launching item"
		];
		
		string matchingMarker = "";
		foreach (string m in marker) {
			if(logLine.StartsWith(m)){
				matchingMarker = m;
				break;
			}
		}
		if(string.IsNullOrWhiteSpace(matchingMarker)){
			return;
		}
		
		string subLine = logLine[matchingMarker.Length..];
		
		string[] subParts = subLine.Trim().Split(' ');
		if(subParts.Length < 2 || !int.TryParse(subParts[0], out int _)){
			return;
		}
		
		string detail = string.Join(' ', subParts[1..]).Trim()
			.Replace("\"", string.Empty);
		
		if(_unprocessedObjects.Remove(detail)){
			_processedObjects.Add(detail);
			OnProgressChanged();
		}
	}
	
	private void OnProgressChanged(){
		int args = ++_processedCount * 100 / _totalCount;
		ProgressChanged?.Invoke(this, args);
	}
}

