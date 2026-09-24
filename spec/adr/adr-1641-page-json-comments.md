# ADR: Use native JSON comment handling (#1641)

Status: accepted

Configure the existing shared marker-section JsonDocument reader with CommentHandling=Skip and AllowTrailingCommas=true. Parse the original text rather than applying the trailing-comma regex, which can modify quoted strings and cannot handle commas separated from closing brackets by comments. Remove the now-unused internal normalizer. Leave direct mobile-body readers unchanged. The original page body remains the write payload.
