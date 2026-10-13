"""Offline provenance audit of a locally installed, explicitly supplied Mono DLL.

Dependencies: pefile, capstone (audit tools only; DraftTG does not use them at runtime).
No DLL loading, process access, target invocation, or proprietary implementation input.
Public semantic reference: Unity-Technologies/mono, commit
75711b4363fa58f05f953b3f95400d4d61c0ca35, mono/metadata and mono/utils.
The source commit is a semantic reference, not a claim of exact build-source identity.
"""
import argparse
import hashlib
import json
from pathlib import Path
import pefile
import capstone

parser = argparse.ArgumentParser()
parser.add_argument("dll", type=Path)
args = parser.parse_args()
raw = args.dll.read_bytes()
digest = hashlib.sha256(raw).hexdigest()
expected = "69dd7ca153d9bd718ed6176c1e6ff065721a9408964384a421602f7d5527a294"
if digest != expected:
    raise SystemExit("Unknown Mono hash; this offset audit profile does not apply: " + digest)
pe = pefile.PE(data=raw)
exports = {s.name.decode(): s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if s.name}
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
# Internal helpers were independently located through exported getter/lookup call paths.
# Explicit lengths include branch targets beyond the first return, not arbitrary memory scans.
targets = {
    "mono_get_root_domain": 32, "mono_assembly_foreach": 240,
    "mono_assembly_get_image": 16, "mono_image_get_name": 16, "mono_image_get_guid": 16,
    "mono_class_get_name": 16, "mono_class_get_namespace": 16, "mono_class_get_image": 16,
    "mono_class_get_parent": 16, "mono_class_get_element_class": 16, "mono_class_get_rank": 16,
    "mono_class_get_fields": 240, "mono_class_instance_size": 80,
    "mono_field_get_name": 16, "mono_field_get_parent": 16, "mono_field_get_offset": 112,
    "mono_domain_get_id": 16, "mono_class_vtable": 240,
    "mono_vtable_get_static_field_data": 80, "mono_object_get_class": 16,
    "mono_array_length": 16, "mono_string_length": 16, "mono_string_chars": 16,
    "class_from_typedef_cache_path": (0xC5480, 600),
    "internal_hash_lookup": (0x71310, 160),
    "class_cache_key_callback": (0x1454D0, 16),
    "class_cache_next_callback": (0x1454E0, 16),
    "class_field_count_helper": (0xCF900, 176),
    "class_setup_fields_readiness": (0xC4DC0, 160),
}
print(json.dumps({"Path": str(args.dll), "SHA256": digest, "Machine": hex(pe.FILE_HEADER.Machine)}, indent=2))
for name, specification in targets.items():
    rva, length = specification if isinstance(specification, tuple) else (exports[name], specification)
    print(f"\n{name} RVA=0x{rva:X}")
    for instruction in decoder.disasm(pe.get_data(rva, length), rva):
        print(f"  {instruction.address:08X}  {instruction.mnemonic} {instruction.op_str}")
