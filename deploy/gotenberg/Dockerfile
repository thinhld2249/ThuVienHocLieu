# Gotenberg 8 + font Times New Roman / Arial / Verdana (tiếng Việt) để chuyển
# Office -> PDF không vỡ bố cục (spec §11.4).
#
# Font: `fonts/*.ttf` nằm sẵn trong repo (msttcorefonts, redistributable —
# cùng nguồn package ttf-mscorefonts-installer của Debian). Build KHÔNG cần mạng.
# `INSTALL_FONTS=true` (build-arg) để apt cài thêm gói font đầy đủ (Comic Sans,
# Georgia, Impact, Webdings…) nếu cần — chỉ dùng khi build có mạng đầy đủ.
FROM gotenberg/gotenberg:8

ENV DEBIAN_FRONTEND=noninteractive

ARG INSTALL_FONTS=false

USER root

RUN if [ "$INSTALL_FONTS" = "true" ]; then \
    apt-get update \
    && apt-get install -y --no-install-recommends wget gpg dirmngr fontconfig ca-certificates \
    && ( sed -i 's/^# \(.*- [[:space:]]*contrib\)$/\1/' /etc/apt/sources.list 2>/dev/null || true ) \
    && ( sed -i 's/^Components: main$/Components: main contrib/' /etc/apt/sources.list.d/debian.sources 2>/dev/null || true ) \
    && apt-get update \
    && echo ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true | debconf-set-selections \
    && apt-get install -y --no-install-recommends ttf-mscorefonts-installer \
    && rm -rf /var/lib/apt/lists/*; \
    fi

COPY fonts/ /usr/share/fonts/truetype/core/
RUN if [ -n "$(ls -A /usr/share/fonts/truetype/core 2>/dev/null)" ]; then fc-cache -f; fi

USER gotenberg

EXPOSE 3000
# Render: web service phải listen trên $PORT (Render inject, mặc định 10000) —
# nếu không health check + traffic của Render rơi vào port sai. docker compose
# không có PORT → 3000 (spec §8.1).
ENTRYPOINT ["/usr/bin/tini", "--", "sh", "-c", "exec gotenberg --api-port=${PORT:-3000}"]
CMD []
