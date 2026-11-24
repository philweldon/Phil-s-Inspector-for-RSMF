# Third-party notices

Phil's Inspector for RSMF files includes the components below. The .NET 10 runtime and Windows Desktop runtime
bundled in the self-contained package are (c) Microsoft Corporation and the .NET Foundation,
MIT licence (https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

| Component | Version | Licence | Copyright / author | Project |
|---|---|---|---|---|
| RSMF manifest schemas (`rsmf_schema_1_0_0.json`, `rsmf_schema_2_0_0.json`) | repo commits ce90d80 / c717cd3 | BSD 3-Clause | kCura LLC / Relativity ODA LLC | https://github.com/relativitydev/rsmf-validator-samples |
| MimeKit | 4.18.2 | MIT | .NET Foundation and Contributors (Jeffrey Stedfast) | https://www.mimekit.net/ |
| BouncyCastle.Cryptography (dependency of MimeKit) | 2.7.0 | MIT | Legion of the Bouncy Castle Inc. 2000-2026 | https://www.bouncycastle.org/ |
| System.Security.Cryptography.Pkcs, System.Formats.Asn1, Microsoft.Bcl.Cryptography (dependencies of MimeKit) | 10.0.13 | MIT | Microsoft Corporation | https://dot.net/ |
| NodaTime (includes the IANA time zone database, public domain) | 3.3.7 | Apache-2.0 | Jon Skeet and contributors | https://nodatime.org/ |
| JsonSchema.Net, Json.More.Net, JsonPointer.Net | 7.4.0, 2.1.1, 5.3.1 | MIT | Greg Dennis | https://github.com/json-everything/json-everything |
| Emoji short-name table (`emoji_short_names.txt`, generated at build time; the package itself is not shipped) | emoji 2.16.0 (Python) | BSD 3-Clause | Taehoon Kim, Kevin Wurster | https://github.com/carpedm20/emoji |
| Humanizer.Core (dependency of JsonSchema.Net) | 2.14.1 | MIT | .NET Foundation and Contributors | https://github.com/Humanizr/Humanizer |

The MIT licence text is at https://licenses.nuget.org/MIT and the Apache licence 2.0 at
https://www.apache.org/licenses/LICENSE-2.0. Licence files shipped inside the packages are
reproduced below.

## RSMF manifest schemas (Relativity validator samples) - licence

The text below is the licence file of the repository the two schema files come from, reproduced
whole. Its last paragraph and the list of `.dll` files concern other files in that repository.
None of those `.dll` files is included in this package; only the two schema files are.

```
Copyright (c) 2016, kCura LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The.dlls listed in this document are made available to you under the terms of a commercial agreement with kCura LLC.  Subject to the terms of that license, You may only use them programmatically to the extent that they are referenced by the other files contained in this package.  You may not access, use, or redistribute them independently of this package.  You may not reverse engineer, decompile, create derivative works, translate, or disassemble these .dlls, and only to the extent that such activity is expressly permitted by applicable law notwithstanding this limitation.  

Relativity.API.dll
kCura.Relativity.Client.dll
Relativity.Kepler.dll
Relativity.Services.DataContracts.dll
Relativity.Services.Interfaces.dll
Relativity.Services.ServiceProxy.dll
kCura.Data.dll
kCura.ImageValidator.dll
kCura.Relativity.OI.FileID.dll
kCura.Relativity.DataReaderClient.dll
kCura.Relativity.ImportAPI.dll
kCura.Utility.dll
kCura.Windows.Forms.dll
kCura.WindEDDS.dll
kCura.WindEDDs.ImportExtension.dll
Relativity.dll
kCura.EventHandler.dll
kCura.Config.dll
kCura.Crypto.dll
kCura.RowDataGateway.dll
Relativity.CustomPages.dll
kCura.Agent.dll
kCura.Config.dll
```

## BouncyCastle.Cryptography - LICENSE.md

```
MIT License (https://opensource.org/licenses/MIT)

Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org).
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sub license, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions: The above copyright notice and this
permission notice shall be included in all copies or substantial portions of the Software.

**THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.**
```

## JsonSchema.Net / Json.More.Net / JsonPointer.Net - LICENSE

```
MIT License

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
