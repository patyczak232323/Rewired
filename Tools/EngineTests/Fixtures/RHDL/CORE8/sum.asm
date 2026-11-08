; 10 + 9 + ... + 1 = 55, kept in memory: the result ends up in [sum] (0x10).
loop:
    LDA sum
    ADD n
    STA sum
    LDA n
    SUB one
    STA n
    JNZ loop
    HLT
n:   DB 10
sum: DB 0
one: DB 1
