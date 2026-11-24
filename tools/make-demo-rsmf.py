#!/usr/bin/env python3
"""Writes the demo conversation used for Store listing pictures: an invented workplace chat as a
valid RSMF 2.0 file: two made-up heroes checking a chat export. All names, addresses and messages are invented.
Usage: make-demo-rsmf.py <out.rsmf>     (needs Pillow for the small chart picture)"""
import base64, io, json, sys, zipfile
from PIL import Image, ImageDraw

def chart():
    img = Image.new("RGB", (360, 200), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.line([(40, 20), (40, 170), (340, 170)], fill=(120, 130, 140), width=2)
    for i, (h, colour) in enumerate([(60, (88, 140, 220)), (95, (88, 140, 220)), (130, (46, 160, 90)), (110, (46, 160, 90))]):
        x = 65 + i * 68
        d.rectangle([x, 170 - h, x + 42, 169], fill=colour)
    buf = io.BytesIO(); img.save(buf, "PNG", optimize=True)
    return buf.getvalue()

def main(out):
    picture = chart()
    people = [("P1", "Captain Deadline"), ("P2", "Kid Backup")]
    participants = [{"id": i, "display": n, "email": n.lower().replace(" ", ".") + "@example.invalid"} for i, n in people]
    def msg(n, who, time, body, **extra):
        return {"type": "message", "body": body, "participant": who, "conversation": "C1",
                "timestamp": f"2026-03-03T{time}:00Z", "id": f"m{n}", **extra}
    events = [
        msg(1, "P1", "14:02", "Kid! The vendor's chat export has arrived. To the review room!"),
        msg(2, "P2", "14:04", "On it, Captain. Do we check the sample files first, or leap in dramatically like last time?", parent="m1"),
        msg(3, "P1", "14:07", "Sample first. Last time we leapt, three files crossed midnight and nobody noticed until Thursday.",
            reactions=[{"value": "thumbsup", "count": 1, "participants": ["P2"]}]),
        msg(4, "P2", "14:15", "Scan done. One file is missing its picture. I suspect foul play. Or a zip error."),
        msg(5, "P2", "14:16", "Here is the breakdown by rule.",
            attachments=[{"id": "findings-by-rule.png", "display": "findings-by-rule.png", "size": len(picture)}]),
        msg(6, "P1", "14:21", "A zip error. For example, the villain never remembers the attachment. Classic.", parent="m4"),
        msg(7, "P2", "14:30", "Shall I send the findings to the vendor, or write them on the moon with a laser?",
            edits=[{"participant": "P2", "timestamp": "2026-03-03T14:31:00Z"}]),
        msg(8, "P1", "14:48", "Send the CSV. Justice is best served as a tidy list. \U0001F389",
            reactions=[{"value": "heart", "count": 1, "participants": ["P2"]}]),
    ]
    manifest = {"version": "2.0.0", "participants": participants,
                "conversations": [{"id": "C1", "display": "Hero hotline", "platform": "Workplace chat", "type": "direct",
                                   "participants": [i for i, _ in people], "custodian": "P1"}],
                "events": events}
    zbuf = io.BytesIO()
    with zipfile.ZipFile(zbuf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("rsmf_manifest.json", json.dumps(manifest, ensure_ascii=False, separators=(",", ":")))
        z.writestr("findings-by-rule.png", picture)
    b64 = base64.encodebytes(zbuf.getvalue()).decode("ascii").replace("\n", "\r\n")
    times = sorted(e["timestamp"] for e in events)
    head = "\r\n".join([
        "X-RSMF-Version: 2.0.0",
        "X-RSMF-Generator: make-demo-rsmf.py",
        f"X-RSMF-BeginDate: {times[0]}",
        f"X-RSMF-EndDate: {times[-1]}",
        f"X-RSMF-EventCount: {len(events)}",
        "From: Captain Deadline <captain.deadline@example.invalid>",
        "To: Kid Backup <kid.backup@example.invalid>",
        "Subject: Hero hotline",
        'Content-Type: multipart/mixed; boundary="_RSMF_Boundary_Delimiter_"',
        "MIME-Version: 1.0", "",
        "--_RSMF_Boundary_Delimiter_",
        'Content-Type: text/plain; charset="utf-8"',
        "Content-Transfer-Encoding: 7bit", "",
        "Invented demo conversation.",
        "--_RSMF_Boundary_Delimiter_",
        'Content-Type: application/zip; name="rsmf.zip"',
        'Content-Disposition: attachment; filename="rsmf.zip"',
        "Content-Transfer-Encoding: base64", "", ""])
    with open(out, "wb") as f:
        f.write((head + b64 + "--_RSMF_Boundary_Delimiter_--\r\n").encode("ascii"))

if __name__ == "__main__":
    main(sys.argv[1])
