# Third-party notices

This repository contains code copied from the projects below. Each copied file names its origin and the changes made
to it in a header comment. The rest of the repository is covered by [LICENSE](LICENSE).

## fsharp-hedgehog

* Source: <https://github.com/hedgehogqa/fsharp-hedgehog>, release commit
  [`a46977278db9a60542e3df3fe0fcd74b90f38ee3`](https://github.com/hedgehogqa/fsharp-hedgehog/tree/a46977278db9a60542e3df3fe0fcd74b90f38ee3)
  (Hedgehog 2.0.4; the repository has no tag for this release)
* Copied into: `tests/Hedgehog.MSTest` (the Hedgehog MSTest adapter), from `src/Hedgehog.NUnit`: `Prelude.fs`,
  `ReflectionHelpers.fs`, `AutoGenConfig.fs`, `IPropertyAttribute.fs`, `RecheckAttribute.fs`, `GenAttribute.fs`,
  `GenAttribute.Prelude.fs`, `PropertyContext.fs`, `InternalLogic.fs`, `PropertiesAttribute.fs` and, adapted from
  NUnit to MSTest, `PropertyAttribute.fs`
* License: Apache License, Version 2.0

```text
Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```
