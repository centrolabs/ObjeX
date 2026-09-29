{{/*
Common name for resources.
*/}}
{{- define "objex.name" -}}
objex
{{- end }}

{{/*
Fullname — uses release name.
*/}}
{{- define "objex.fullname" -}}
{{ .Release.Name }}
{{- end }}

{{/*
Chart label value.
*/}}
{{- define "objex.chart" -}}
{{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
{{- end }}

{{/*
Common labels applied to all resources.
*/}}
{{- define "objex.labels" -}}
helm.sh/chart: {{ include "objex.chart" . }}
{{ include "objex.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels — used in both metadata.labels and spec.selector.matchLabels.
*/}}
{{- define "objex.selectorLabels" -}}
app.kubernetes.io/name: {{ include "objex.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Service account name.
*/}}
{{- define "objex.serviceAccountName" -}}
{{ include "objex.fullname" . }}
{{- end }}

{{/*
Secret name.
*/}}
{{- define "objex.secretName" -}}
{{ include "objex.fullname" . }}
{{- end }}
