{{- define "clio-mcp-server.fullname" -}}
{{- .Values.fullnameOverride | default (printf "%s-clio-mcp-server" .Release.Name) -}}
{{- end -}}
