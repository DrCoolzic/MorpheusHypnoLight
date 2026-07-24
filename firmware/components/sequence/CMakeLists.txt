idf_component_register(SRCS "sequence_compact.c" "sequence.c"
                       INCLUDE_DIRS "include"
                       REQUIRES freertos oscillator modulator led_engine esp_timer)
