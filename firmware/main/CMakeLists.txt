idf_component_register(SRCS "main.c" "test.c" "test_sequence.c"
                       INCLUDE_DIRS "."
                       REQUIRES esp_timer led_control oscillator modulator led_engine sequence console esp_driver_uart)

# Auto-regenerate test_sequence.c/h from the JSON description before building.
set(DEMO_JSON "${CMAKE_CURRENT_SOURCE_DIR}/../sequences/demo.json")
set(GENERATOR "${CMAKE_CURRENT_SOURCE_DIR}/../scripts/generate_sequence.py")
set(TEST_SEQUENCE_C "${CMAKE_CURRENT_SOURCE_DIR}/test_sequence.c")
set(TEST_SEQUENCE_H "${CMAKE_CURRENT_SOURCE_DIR}/test_sequence.h")

add_custom_command(
    OUTPUT ${TEST_SEQUENCE_C} ${TEST_SEQUENCE_H}
    COMMAND python "${GENERATOR}" "${DEMO_JSON}"
    DEPENDS ${DEMO_JSON} ${GENERATOR}
    COMMENT "Generating test_sequence.c/h from ${DEMO_JSON}"
    VERBATIM)

add_custom_target(generate_test_sequence DEPENDS ${TEST_SEQUENCE_C} ${TEST_SEQUENCE_H})
add_dependencies(${COMPONENT_LIB} generate_test_sequence)