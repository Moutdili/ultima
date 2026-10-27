# ULTIMA — raccourcis. Tout passe par podman par défaut (reproductible).
IMAGE      ?= ultima-toolchain:local
PODMAN     ?= podman
GAME_DIR   ?= $(HOME)/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2
SEL        ?= z

.PHONY: help image build test ci deploy doctor shell decompile index clean

help:
	@echo "make image     : construit l'image toolchain"
	@echo "make build     : compile le mod (sans le jeu dans l'image, via volume)"
	@echo "make test      : tests unitaires Core (aucun jeu requis)"
	@echo "make ci        : build + test + grep read-only"
	@echo "make deploy    : copie le mod dans le dossier mods du jeu"
	@echo "make doctor    : diagnostic hôte (jeu, mods, toolchain locale)"
	@echo "make shell     : shell dans le conteneur avec le repo monté"
	@echo "make decompile : GDRE + ILSpy (nécessite le jeu monté)"
	@echo "make index     : régénère data/catalog"
	@echo "make clean     : nettoie bin/obj/build"

image:
	$(PODMAN) build -t $(IMAGE) -f containers/Containerfile .

build:
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work \
	  -v "$(GAME_DIR):/game:ro,$(SEL)" -e STS2_DATA=/game/data_sts2_linuxbsd_x86_64 \
	  $(IMAGE) bash tools/build.sh

test:
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work $(IMAGE) \
	  dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release

ci: test
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work $(IMAGE) bash -c \
	  '! grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs | grep -v "^\s*//"'

deploy:
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work \
	  -v "$(GAME_DIR):/game:z" -e STS2_DATA=/game/data_sts2_linuxbsd_x86_64 \
	  $(IMAGE) bash tools/deploy.sh

doctor:
	bash tools/doctor.sh

shell:
	$(PODMAN) run --rm -it -v "$$PWD:/work:$(SEL)" -w /work $(IMAGE) bash

decompile:
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work \
	  -v "$(GAME_DIR):/game:ro,$(SEL)" -e STS2_GAME=/game \
	  $(IMAGE) bash tools/decompile.sh

index:
	$(PODMAN) run --rm -v "$$PWD:/work:$(SEL)" -w /work $(IMAGE) \
	  python3 tools/index_source.py --src game/pck/src/Core --out data/catalog --repo .

clean:
	rm -rf Ultima/bin Ultima/obj Ultima.Core/bin Ultima.Core/obj Ultima.Tests/bin Ultima.Tests/obj build
