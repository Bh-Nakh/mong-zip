# 제3자 구성요소 고지 (THIRD-PARTY NOTICES)

몽집은 아래 제3자 구성요소를 함께 배포한다. 각 구성요소는 해당 저작권자의 라이선스를 따르며, 그 조건은 몽집의 GPL-3.0 라이선스(`LICENSE`)와 별개로 유지된다.

## 구성요소 목록

| 구성요소 | 버전 | 라이선스 | 출처 | 고지 파일 위치 |
|---|---|---|---|---|
| 7-Zip (`engine/7z.exe`, `engine/7z.dll`) | 26.04 | LGPL-2.1-or-later, 일부 코드는 unRAR license restriction 적용, 일부 코드는 BSD-3-Clause / BSD-2-Clause (7z.dll) | https://www.7-zip.org/ (수정 없이 그대로 번들) | `engine/7zip-License.txt` |

## 7-Zip

7-Zip Copyright (C) 1999-2026 Igor Pavlov.

몽집은 7-Zip의 소스를 몽집 코드에 섞지 않는다. 7-Zip 공식 배포 바이너리(`7z.exe`, `7z.dll`)를 수정 없이 `engine/` 폴더에 번들하고, 몽집이 `7z.exe`를 별도 프로세스로 호출한다. 몽집 소스(GPL-3.0)와 7-Zip은 서로 다른 독립 저작물이며, 7-Zip은 자기 라이선스를 그대로 유지한다. 7-Zip의 라이선스 원문은 `engine/7zip-License.txt`에 받은 그대로 보관하며, 설치본에도 같은 파일을 함께 설치한다(바이너리 재배포 시 라이선스 정보를 함께 두라는 조건을 따른다).

파일별 라이선스는 `engine/7zip-License.txt` 의 설명을 따른다.

- `7z.dll`: 대부분의 코드는 GNU LGPL, 일부는 "unRAR license restriction"이 붙은 GNU LGPL, 일부는 BSD 3-clause, 일부는 BSD 2-clause
- 그 밖의 파일: GNU LGPL

### unRAR 라이선스 제한

7-Zip의 RAR 해제 엔진은 unRAR 프로그램의 소스 코드를 바탕으로 만들어졌다. 원본 unRAR 코드의 저작권은 Alexander Roshal에게 있다. 이 코드는 RAR(WinRAR) 호환 압축기를 개발하는 데 사용할 수 없다. 원문은 다음과 같다.

> The unRAR sources cannot be used to re-create the RAR compression algorithm, which is proprietary. Distribution of modified unRAR sources in separate form or as a part of other software is permitted, provided that it is clearly stated in the documentation and source comments that the code may not be used to develop a RAR (WinRAR) compatible archiver.

몽집은 RAR 압축 기능이 없고, RAR 파일은 번들 `7z.dll`로 해제만 한다. 몽집의 소스 코드는 unRAR 코드를 포함하지 않는다. 이 제한은 7-Zip 안의 unRAR 유래 코드에만 해당하며 몽집 소스에는 붙지 않는다.

### 7-Zip 소스 코드 제공

7-Zip의 소스 코드는 https://www.7-zip.org/ 에서 받을 수 있다(LGPL 조건에 따른 소스 제공 방법). 몽집은 7-Zip을 수정하지 않았다.

## 미결 사항

- 없음. 7-Zip unRAR 제한 조항은 몽집이 RAR 압축기를 만들지 않고 unRAR 소스를 직접 쓰지 않으므로 코드 제거 없이 문서에 명시하는 것으로 처리했다. (법률 최종 판단이 아니며 공개 전 전문가 확인을 권장한다.)

## 라이선스 원문 보관 위치

- 7-Zip: 저장소 `engine/7zip-License.txt` (설치 폴더에도 `7zip-License.txt`로 설치된다). 원문은 수정하지 않고 받은 그대로 둔다.
