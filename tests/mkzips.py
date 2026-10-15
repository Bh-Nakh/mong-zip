# 시험용 특수 ZIP 생성 (Zip Slip, CP949 파일명, 압축 폭탄, 폴더 구조 변형, 손상본)
import os
import sys
import zipfile

w = sys.argv[1]


def p(n):
    return os.path.join(w, n)


# 1) Zip Slip
with zipfile.ZipFile(p("slip.zip"), "w") as z:
    z.writestr("good.txt", "ok")
    z.writestr("../evil.txt", "bad")
    z.writestr("sub/../../evil2.txt", "bad2")

# 2) CP949 파일명(UTF-8 플래그 없음): 같은 길이의 자리표시자를 만든 뒤 바이트를 교체
with zipfile.ZipFile(p("cp949.zip"), "w") as z:
    z.writestr("XXXXXXXX", "korean-content")
raw = open(p("cp949.zip"), "rb").read()
raw = raw.replace(b"XXXXXXXX", "한글.txt".encode("cp949"))
open(p("cp949.zip"), "wb").write(raw)

# 3) 압축 폭탄 (0 으로 채운 5MB)
with zipfile.ZipFile(p("bomb.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("zeros.bin", b"\0" * (5 * 1024 * 1024))

# 4) 최상위가 둘인 압축파일
with zipfile.ZipFile(p("two_tops.zip"), "w") as z:
    z.writestr("a.txt", "A")
    z.writestr("b.txt", "B")

# 5) 최상위 폴더가 하나인 압축파일
with zipfile.ZipFile(p("one_top.zip"), "w") as z:
    z.writestr("only/x.txt", "X")
    z.writestr("only/inner/y.txt", "Y")

# 6) 손상본: 정상 압축파일 뒤쪽을 잘라 낸다
with zipfile.ZipFile(p("broken_src.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("data.txt", "hello world " * 2000)
b = open(p("broken_src.zip"), "rb").read()
open(p("broken.zip"), "wb").write(b[: len(b) // 2])
os.remove(p("broken_src.zip"))

# 7) 절대 경로 항목
with zipfile.ZipFile(p("abs.zip"), "w") as z:
    z.writestr("fine.txt", "fine")
    zi = zipfile.ZipInfo("/tmp_abs_evil.txt")
    z.writestr(zi, "abs")
