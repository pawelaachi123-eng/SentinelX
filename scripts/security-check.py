#!/usr/bin/env python3
import hashlib,re,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
tracked=subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0')
errors=[]
for name in tracked:
 if not name:continue
 p=root/name
 if not p.is_file():continue
 if p.suffix.lower() in ('.p12','.pfx','.jks','.keystore','.pem','.key'):errors.append('Private key material: '+name)
 if p.suffix.lower() in ('.cs','.java','.gradle','.yml','.js','.json','.ps1'):
  text=p.read_text(errors='replace')
  if re.search(r'(storePassword|keyPassword)\s+[\'"][^\'"]+[\'"]',text):errors.append('Hardcoded signing password: '+name)
  if re.search(r'-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----',text):errors.append('Private key: '+name)
  if re.search(r'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}',text):errors.append('GitHub credential: '+name)
android=(root/'phone-android/app/build.gradle').read_text()
assert 'abortOnError true' in android and 'checkReleaseBuilds true' in android,'Lint must fail on errors'
assert "versionName '1.0.0'" in android and 'versionCode 100' in android,'Version mismatch'
assert (root/'VERSION').read_text().strip()=='1.0.0'
assert 'distributionSha256Sum=d725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab' in (root/'phone-android/gradle/wrapper/gradle-wrapper.properties').read_text(),'Wrapper checksum'
session=(root/'phone-android/app/src/main/java/pl/sentinelx/phone/Session.java').read_text()
assert 'return secrets.get("pc.token")' in session,'Plain token storage'
release=(root/'.github/workflows/release.yml').read_text()
assert 'needs: [security, windows, android]' in release and release.count('gh release create')==2,'Single aggregate publisher'
assert 'update-manifest' not in release.lower(),'User requested no update manifest'
assert not errors,'\n'.join(errors)
print('PASS version, secret scan, Keystore token, wrapper integrity, strict lint and release gates')
