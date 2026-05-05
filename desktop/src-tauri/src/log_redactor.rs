// PRD DESK-010 — PHI redaction in local logs.
//
// Best-effort regex-based redactor wired into the global tracing subscriber
// via a `MakeWriter` shim. Anything that looks like a name token (`name:
// John Doe`), a date adjacent to a NAME/DOB token, or a 6-12 digit run
// (MRN-like) is replaced with `<redacted:phi>`. ICD-10 codes — letters +
// digits + optional period — pass because the MRN regex demands purely
// numeric runs.
//
// This is defence-in-depth. The primary control is "do not log PHI";
// the redactor exists so that a bug elsewhere in the stack cannot leak
// patient data to the local rolling log.

use std::io;
use std::sync::OnceLock;

use regex::Regex;
use tracing_subscriber::fmt::MakeWriter;

static NAME_RE: OnceLock<Regex> = OnceLock::new();
static DATE_NEAR_NAME_RE: OnceLock<Regex> = OnceLock::new();
static MRN_RE: OnceLock<Regex> = OnceLock::new();

/// Redact likely-PHI substrings in `input`. Idempotent and lossy.
pub fn redact(input: &str) -> String {
    let name = NAME_RE.get_or_init(|| {
        Regex::new(
            r"(?i)\b(?:patient|name|pt)\s*[:=]\s*[A-Z][A-Za-z'\-]+(?:\s+[A-Z][A-Za-z'\-]+)+",
        )
        .expect("name regex")
    });
    let date_near = DATE_NEAR_NAME_RE.get_or_init(|| {
        Regex::new(
            r"(?i)\b(?:dob|name|patient)\s*[:=]?\s*\d{1,4}[\-/.]\d{1,2}[\-/.]\d{1,4}",
        )
        .expect("date regex")
    });
    let mrn = MRN_RE.get_or_init(|| Regex::new(r"\b\d{6,12}\b").expect("mrn regex"));

    let s = name.replace_all(input, "<redacted:phi>");
    let s = date_near.replace_all(&s, "<redacted:phi>");
    let s = mrn.replace_all(&s, "<redacted:phi>");
    s.into_owned()
}

/// `io::Write` adapter that redacts every chunk before forwarding to stderr.
pub struct RedactingWriter;

impl io::Write for RedactingWriter {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        // We deliberately accept the cost of UTF-8 conversion: the input is
        // human-readable log lines from the tracing fmt layer.
        let s = String::from_utf8_lossy(buf);
        let red = redact(&s);
        let mut err = io::stderr().lock();
        err.write_all(red.as_bytes())?;
        Ok(buf.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        io::stderr().flush()
    }
}

impl<'a> MakeWriter<'a> for RedactingWriter {
    type Writer = RedactingWriter;
    fn make_writer(&'a self) -> Self::Writer {
        RedactingWriter
    }
}

/// Install the redacting writer as the global tracing subscriber.
/// Safe to call multiple times — subsequent calls are no-ops.
pub fn init() {
    let _ = tracing_subscriber::fmt()
        .with_writer(RedactingWriter)
        .with_target(false)
        .try_init();
}

#[cfg(test)]
mod tests {
    use super::redact;

    #[test]
    fn redacts_mrn_like_runs() {
        let r = redact("ingest mrn=123456789 ok");
        assert!(r.contains("<redacted:phi>"));
        assert!(!r.contains("123456789"));
    }

    #[test]
    fn redacts_name_token() {
        let r = redact("Patient: Jane Doe scheduled");
        assert!(r.contains("<redacted:phi>"));
    }

    #[test]
    fn passes_icd10_codes() {
        let r = redact("finding C50.911 confirmed");
        assert!(r.contains("C50.911"));
    }

    #[test]
    fn passes_short_numbers() {
        let r = redact("status=200 took=42ms");
        assert_eq!(r, "status=200 took=42ms");
    }
}
