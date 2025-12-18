Task: Fix our IIS discovery logic in `IISScannerHandler`. Currently we only find top level sites and completely ignore nested(inner) sites. 


Goal: Update code to discover top lvel sites as is, as well as all inner sites.


How to get data (use appcmd):

Sites:
appcmd list site /xml

Applications (all or per-site):
appcmd list app /xml
and/or appcmd list app /site.name:"{siteName}" /xml

Virtual directories (all or per-site):
appcmd list vdir /xml
and/or appcmd list vdir /site.name:"{siteName}" /xml

Implementation details:

Use our existing command-execution

Parse XML output robustly.

Normalize paths so you can join correctly (site name + app path; app path always begins with /).

