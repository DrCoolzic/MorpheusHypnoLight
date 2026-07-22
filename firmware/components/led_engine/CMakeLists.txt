idf_component_register(SRCS "led_engine.c"
                       INCLUDE_DIRS "include"
                       REQUIRES modulator oscillator led_control esp_common freertos)
